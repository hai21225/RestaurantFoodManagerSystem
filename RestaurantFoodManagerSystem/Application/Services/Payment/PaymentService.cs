using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Nghiệp vụ thanh toán.
///
/// <para>2 loại thu tiền:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Trực tiếp</b> — <see cref="CreateAsync"/>: thu tiền mặt/thẻ nội bộ, đóng đơn ngay.
/// </description></item>
/// <item><description>
/// <b>Qua provider</b> (MoMo) — <see cref="CreateMomoIntentAsync"/> tạo "intent"
/// (<c>Status = Pending</c>), khách trả qua QR, MoMo gọi IPN về
/// (<see cref="ApplyMomoNotificationAsync"/>) thì mới đóng đơn. Có thể hoàn tiền
/// (<see cref="RefundAsync"/>).
/// </description></item>
/// </list>
///
/// <para>
/// Luật bất di bất dịch: **số tiền luôn lấy từ đơn**, không tin giá trị client gửi lên;
/// IPN sai chữ ký hoặc sai số tiền đều bị từ chối; IPN gửi lại nhiều lần là idempotent.
/// </para>
/// </summary>
public sealed class PaymentService
{
    private readonly AppDbContext _db;
    private readonly RestaurantTableService _tables;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PaymentService> _logger;
    private readonly OrderLifecycleCoordinator _lifecycle;
    private readonly IOrderIdempotencyStore _idempotency;
    private readonly MomoPaymentService _momo;
    private readonly PaymentOptions _options;

    public PaymentService(
        AppDbContext db,
        RestaurantTableService tables,
        ICurrentUser currentUser,
        ILogger<PaymentService> logger,
        OrderLifecycleCoordinator lifecycle,
        IOrderIdempotencyStore idempotency,
        MomoPaymentService momo,
        PaymentOptions options)
    {
        _db = db;
        _tables = tables;
        _currentUser = currentUser;
        _logger = logger;
        _lifecycle = lifecycle;
        _idempotency = idempotency;
        _momo = momo;
        _options = options;
    }

    // ==========================================================
    // Thu tiền trực tiếp
    // ==========================================================

    /// <summary>
    /// Ghi nhận thanh toán, hỗ trợ header <c>Idempotency-Key</c> (Đợt 2 / A5).
    ///
    /// <para>
    /// Bấm "thu tiền" 2 lần (double-click, retry mạng) chỉ tạo **một** bản ghi payment
    /// và đánh dấu đơn là <c>Paid</c> **một** lần.
    /// </para>
    /// </summary>
    public async Task<Payment?> CreateIdempotentAsync(
        CreatePaymentRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await CreateAsync(request, ct);
        }

        var key = idempotencyKey.Trim();
        if (key.Length > 128)
        {
            throw new ArgumentException("Idempotency-Key must be at most 128 characters");
        }

        var scope = (_currentUser.UserId ?? request.CreatedBy).ToString();
        var fingerprint = BuildFingerprint("payments", request);

        return await _idempotency.ExecuteAsync<Payment?>(
            $"{scope}:payments:{key}",
            fingerprint,
            token => CreateAsync(request, token),
            ct);
    }

    /// <summary>
    /// Fingerprint của payload thanh toán: SHA-256 của <c>scope + JSON payload</c>.
    /// Cùng key nhưng payload khác ⇒ báo lỗi (409) thay vì trả kết quả cũ.
    /// </summary>
    private static string BuildFingerprint(string scope, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes($"{scope}|{json}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public async Task<Payment?> CreateAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        // Validate thuần (không cần DB) trước transaction để không giữ lock không cần thiết.
        // Defect #26: PaymentMethod không chỉ Trim mà phải thuộc allow-list và được chuẩn hoá
        // (frontend gửi "cash"/"card"/"bank"/"ewallet"; API cũ dùng "Cash"/"Card").
        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);

        // Nhóm B4: payment + order-status phải là nguyên tử. Mọi kiểm tra phải nằm TRONG
        // transaction Serializable — trước đây đọc đơn và kiểm tra ở NGOÀI transaction nên
        // 2 request cùng lúc đều vượt qua kiểm tra "đã trả tiền chưa". (P0-09)
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == request.OrderId, ct);
        if (order is null) return null;

        // Nhóm B: không thanh toán đơn đã huỷ; không thanh toán trùng.
        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot pay a cancelled order");
        if (string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Order is already paid");
        if (await _db.Payments.AnyAsync(
                x => x.OrderId == request.OrderId && x.Status == PaymentStatuses.Paid, ct))
            throw new InvalidOperationException("Order is already paid");

        // P1e: không thu tiền khi bếp chưa xong — tránh trả bàn sớm trong lúc món còn Preparing.
        // Đơn không có dòng món (dữ liệu legacy) giữ nguyên hành vi cũ.
        var itemStatuses = await _db.OrderItems.AsNoTracking()
            .Where(x => x.OrderId == request.OrderId)
            .Select(x => x.Status)
            .ToListAsync(ct);

        if (itemStatuses.Count > 0)
        {
            var activeItems = itemStatuses
                .Where(x => !KitchenStatuses.IsCancelled(x))
                .ToList();

            if (activeItems.Count == 0)
                throw new InvalidOperationException("Order has no payable items");

            if (activeItems.Any(x => !KitchenStatuses.IsDone(x)) ||
                !OrderStatuses.IsPayable(order.Status))
            {
                throw new InvalidOperationException("Order is not ready for payment");
            }
        }

        var createdBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.CreatedBy, _logger, nameof(request.CreatedBy), ct);

        // Nhóm B3: đơn tổng 0đ không phải là một khoản thu; tránh tạo payment "Paid" vô nghĩa.
        if (order.TotalAmount <= 0)
            throw new InvalidOperationException("Order total must be greater than zero");

        var now = DateTime.UtcNow;

        // C-19: mã giao dịch phải **luôn** có và phải duy nhất. Client không gửi thì
        // server sinh mã nội bộ; client gửi mã trùng ⇒ 409 (chống thu 2 lần cùng mã).
        var transactionCode = PaymentTransactionCodes.Normalize(request.TransactionCode);
        if (transactionCode is not null
            && await _db.Payments.AnyAsync(x => x.TransactionCode == transactionCode, ct))
        {
            throw new InvalidOperationException(
                $"Transaction code '{transactionCode}' already exists");
        }

        var payment = new Payment
        {
            OrderId = order.OrderId,
            Amount = order.TotalAmount,
            PaymentMethod = paymentMethod,
            Provider = PaymentProviders.Direct,
            Status = PaymentStatuses.Paid,
            TransactionCode = transactionCode ?? PaymentTransactionCodes.Generate(now),
            PaidAt = now,
            CreatedAt = now,
            CreatedBy = createdBy
        };
        _db.Payments.Add(payment);

        // P0-02: đi qua coordinator để đơn có dòng lịch sử "Ready → Paid" thay vì gán thẳng.
        _lifecycle.ApplyPaid(order, createdBy, $"Payment via {paymentMethod}");

        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Nhóm B3: unique index lọc (OrderId, Status='Paid') là chốt chặn cuối khi hai request
            // đồng thời cùng vượt qua kiểm tra phía trên. Phân biệt lỗi trùng thanh toán với lỗi
            // khác (FK...) bằng cách truy vấn lại sau khi rollback.
            try { await tx.RollbackAsync(ct); } catch (InvalidOperationException) { /* tx đã đóng */ }

            _db.ChangeTracker.Clear();

            if (await _db.Payments.AsNoTracking().AnyAsync(
                    x => x.OrderId == order.OrderId && x.Status == PaymentStatuses.Paid, ct))
                throw new InvalidOperationException("Order is already paid", ex);

            // C-19: unique index trên TransactionCode ⇒ 2 request cùng mã ngân hàng
            // chạy song song thì 1 thành công, 1 phải báo trùng mã (409) chứ không phải 500.
            if (transactionCode is not null
                && await _db.Payments.AsNoTracking().AnyAsync(
                    x => x.TransactionCode == transactionCode, ct))
            {
                throw new InvalidOperationException(
                    $"Transaction code '{transactionCode}' already exists", ex);
            }

            throw;
        }

        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return payment;
    }

    /// <summary>
    /// Allow-list phương thức thanh toán (Defect #26). So khớp không phân biệt hoa/thường và
    /// trả về tên chuẩn để DB không chứa lẫn lộn "cash", "Cash", "CASH".
    /// </summary>
    private static readonly string[] AllowedPaymentMethods = ["Cash", "Card", "Bank", "EWallet", "Transfer"];

    private static string NormalizePaymentMethod(string? raw)
    {
        var method = raw?.Trim() ?? string.Empty;
        if (method.Length == 0)
            throw new ArgumentException("Payment method is required");

        var canonical = AllowedPaymentMethods.FirstOrDefault(
            x => string.Equals(x, method, StringComparison.OrdinalIgnoreCase));

        return canonical
            ?? throw new ArgumentException($"Invalid payment method: {method}");
    }

    // ==========================================================
    // Luồng provider: MoMo (intent → QR → IPN)
    // ==========================================================

    /// <summary>
    /// Tạo giao dịch MoMo cho một đơn. Trả về URL để khách trả (trang MoMo, hoặc endpoint
    /// giả lập khi chạy ở chế độ <c>Mock</c>). Đơn **chưa** được đánh dấu <c>Paid</c>.
    ///
    /// <para>Bấm 2 lần không tạo 2 giao dịch: nếu đơn đã có intent MoMo đang chờ thì trả lại
    /// intent đó (cùng QR) — tránh tình trạng khách quét nhầm QR cũ.</para>
    /// </summary>
    public async Task<MomoIntentResponse?> CreateMomoIntentAsync(
        CreateMomoPaymentRequest request,
        CancellationToken ct)
    {
        if (!_momo.IsMockMode && !_momo.HasRealCredentials)
        {
            throw new InvalidOperationException(
                "MoMo is not configured. Set Payments:MoMo:PartnerCode/AccessKey/SecretKey, "
                + "or use Payments:Provider=Mock for the test environment.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == request.OrderId, ct);
        if (order is null) return null;

        if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot pay a cancelled order");
        if (string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Order is already paid");
        if (!OrderStatuses.IsPayable(order.Status))
            throw new InvalidOperationException("Order is not ready for payment");

        var createdBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.CreatedBy, _logger, nameof(request.CreatedBy), ct);

        var pending = await _db.Payments.FirstOrDefaultAsync(
            x => x.OrderId == order.OrderId
              && x.Provider == PaymentProviders.MoMo
              && x.Status == PaymentStatuses.Pending,
            ct);

        if (pending is not null)
        {
            var reused = ToIntentResponse(pending);
            await tx.CommitAsync(ct);
            return reused;
        }

        var now = DateTime.UtcNow;
        var requestId = Guid.NewGuid().ToString("N");

        var payment = new Payment
        {
            OrderId = order.OrderId,
            Amount = order.TotalAmount,
            PaymentMethod = PaymentProviders.MoMo,
            Provider = PaymentProviders.MoMo,
            Status = PaymentStatuses.Pending,
            // C-19: intent MoMo cũng cần mã giao dịch riêng (mã của MoMo nằm ở
            // ProviderTransactionId, ghi khi IPN về).
            TransactionCode = PaymentTransactionCodes.Generate(now),
            RequestId = requestId,
            CreatedAt = now,
            CreatedBy = createdBy
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var create = _momo.BuildCreatePayload(
            payment.Amount,
            order.OrderId.ToString(),
            MomoPaymentService.BuildOrderInfo(order.OrderId),
            requestId,
            order.OrderId.ToString(),
            payment.PaymentId.ToString());

        if (_momo.IsMockMode)
        {
            // Chế độ giả lập: KHÔNG gọi ra ngoài. URL trả về trỏ về endpoint giả lập của
            // chính backend — gọi nó là đóng tiền thành công, không tốn tiền thật.
            payment.ProviderResponse = JsonSerializer.Serialize(
                new MomoPaymentService.MomoCreateResponse
                {
                    PartnerCode = create.PartnerCode,
                    PartnerRefCode = create.PartnerRefCode,
                    Amount = create.Amount,
                    OrderId = create.OrderId,
                    OrderInfo = create.OrderInfo,
                    RedirectUrl = BuildMockRedirectUrl(payment.PaymentId),
                    QrCodeUrl = string.Empty,
                    RequestId = create.RequestId,
                    RequestType = create.RequestType,
                    NotificationUrl = create.NotificationUrl,
                    ResponseCode = 0,
                    Message = "Mock intent created"
                });
        }
        else
        {
            var result = await _momo.CreateAsync(create, ct);
            if (!result.Success)
            {
                payment.Status = PaymentStatuses.Failed;
                payment.Note = result.Error;
                payment.ProviderResponse = result.RawResponse;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                throw new InvalidOperationException($"MoMo could not create the payment: {result.Error}");
            }

            payment.ProviderResponse = result.RawResponse;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToIntentResponse(payment);
    }

    /// <summary>
    /// Xử lý IPN (callback) từ MoMo.
    ///
    /// <para>Thứ tự kiểm tra (không được đảo):</para>
    /// <list type="number">
    /// <item><description>Chữ ký HMAC — sai ⇒ từ chối.</description></item>
    /// <item><description>Tìm giao dịch theo <c>requestId</c> — không có ⇒ từ chối.</description></item>
    /// <item><description>Đã <c>Paid</c> rồi ⇒ trả về thành công (MoMo gửi lại nhiều lần), không ghi 2 lần.</description></item>
    /// <item><description><c>resultCode ≠ 9000</c> ⇒ đánh dấu <c>Failed</c>.</description></item>
    /// <item><description>Số tiền phải khớp <b>cả</b> giao dịch lẫn tổng đơn — không tin client.</description></item>
    /// </list>
    /// </summary>
    public async Task<MomoNotificationOutcome> ApplyMomoNotificationAsync(
        MomoNotificationRequest request,
        CancellationToken ct)
    {
        if (!_momo.VerifyNotification(request))
        {
            _logger.LogWarning("MoMo notification rejected: invalid signature");
            return MomoNotificationOutcome.Rejected("Invalid signature");
        }

        var payment = await _db.Payments.FirstOrDefaultAsync(
            x => x.RequestId == request.RequestId, ct);

        if (payment is null)
        {
            _logger.LogWarning("MoMo notification rejected: unknown requestId {RequestId}", request.RequestId);
            return MomoNotificationOutcome.Rejected("Unknown payment");
        }

        if (payment.Status == PaymentStatuses.Paid)
        {
            // Idempotency: MoMo gửi lại IPN nhiều lần là chuyện thường.
            return MomoNotificationOutcome.AlreadyApplied(payment.PaymentId);
        }

        if (payment.Status == PaymentStatuses.Refunded)
        {
            return MomoNotificationOutcome.Rejected("Payment was refunded");
        }

        if (!request.IsSuccess)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.Note = request.Message;
            await _db.SaveChangesAsync(ct);
            return MomoNotificationOutcome.Failed(payment.PaymentId, request.Message ?? "Payment failed");
        }

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == payment.OrderId, ct);
        if (order is null)
        {
            return MomoNotificationOutcome.Rejected("Order not found");
        }

        if (string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
        {
            return MomoNotificationOutcome.Rejected("Order is already paid");
        }

        // Số tiền phải khớp: vừa giao dịch, vừa tổng đơn hiện tại.
        var notifiedAmount = MomoPaymentService.FormatAmount(
            decimal.TryParse(request.Amount, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m);

        if (!string.Equals(notifiedAmount, MomoPaymentService.FormatAmount(payment.Amount), StringComparison.Ordinal)
            || order.TotalAmount != payment.Amount)
        {
            _logger.LogError(
                "MoMo notification rejected: amount mismatch (notified={Notified}, payment={Payment}, order={Order})",
                notifiedAmount, payment.Amount, order.TotalAmount);
            return MomoNotificationOutcome.Rejected("Amount mismatch");
        }

        payment.Status = PaymentStatuses.Paid;
        payment.PaidAt = DateTime.UtcNow;
        payment.ProviderTransactionId = request.TransId;
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            payment.Note = request.Message;
        }

        _lifecycle.ApplyPaid(order, payment.CreatedBy, $"Payment via MoMo ({request.TransId})");

        await _db.SaveChangesAsync(ct);
        await _tables.RefreshStatusForTableAsync(order.TableId, ct);

        return MomoNotificationOutcome.Applied(payment.PaymentId);
    }

    /// <summary>
    /// Giả lập kết quả thanh toán (chỉ dùng ở chế độ <c>Mock</c>).
    ///
    /// <para>Payload IPN được sinh ra **đã ký chữ ký đúng** rồi đi qua <b>đúng</b> đường
    /// verify của IPN thật ⇒ sai chữ ký / sai số tiền / trùng IPN đều bị bắt như ngoài đời.</para>
    /// </summary>
    public async Task<MomoNotificationOutcome> SimulateMomoPaymentAsync(
        int paymentId,
        bool success,
        CancellationToken ct)
    {
        if (!_momo.IsMockMode)
        {
            throw new InvalidOperationException(
                "Simulated payments are only available when Payments:Provider=Mock.");
        }

        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.PaymentId == paymentId, ct);
        if (payment is null)
        {
            return MomoNotificationOutcome.Rejected("Payment not found");
        }

        if (!string.Equals(payment.Provider, PaymentProviders.MoMo, StringComparison.OrdinalIgnoreCase))
        {
            return MomoNotificationOutcome.Rejected("Payment is not a MoMo payment");
        }

        var create = _momo.BuildCreatePayload(
            payment.Amount,
            payment.OrderId.ToString(),
            MomoPaymentService.BuildOrderInfo(payment.OrderId),
            payment.RequestId ?? string.Empty,
            payment.OrderId.ToString(),
            payment.PaymentId.ToString());

        var notification = _momo.BuildMockNotification(
            create,
            success,
            success ? "Mock payment succeeded" : "Mock payment failed");

        return await ApplyMomoNotificationAsync(
            new MomoNotificationRequest
            {
                PartnerCode = notification.PartnerCode,
                PartnerRefCode = notification.PartnerRefCode,
                Amount = notification.Amount,
                OrderId = notification.OrderId,
                OrderInfo = notification.OrderInfo,
                RequestId = notification.RequestId,
                RequestType = notification.RequestType,
                NotificationUrl = notification.NotificationUrl,
                ResultCode = notification.ResultCode,
                ResponseCode = notification.ResponseCode,
                ExtraData = notification.ExtraData,
                Message = notification.Message,
                TransId = notification.TransId,
                ResponseTime = notification.ResponseTime,
                Signature = notification.Signature
            },
            ct);
    }

    // ==========================================================
    // Hoàn tiền
    // ==========================================================

    /// <summary>
    /// Hoàn tiền một giao dịch đã thu (chỉ hoàn **toàn bộ**).
    ///
    /// <para>
    /// Đơn được mở lại về <c>Ready</c> (đã phục vụ xong, chờ thu lại) và bàn trở lại
    /// <c>Occupied</c>. Lịch sử đơn ghi thêm dòng "đã hoàn tiền".
    /// </para>
    /// </summary>
    public async Task<PaymentResponse?> RefundAsync(
        int paymentId,
        RefundPaymentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Refund reason is required");
        if (request.Reason.Trim().Length > 500)
            throw new ArgumentException("Refund reason must be at most 500 characters");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.PaymentId == paymentId, ct);
        if (payment is null) return null;

        if (payment.Status != PaymentStatuses.Paid)
            throw new InvalidOperationException(
                $"Payment is {payment.Status} and cannot be refunded");

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.OrderId == payment.OrderId, ct);
        if (order is null)
            throw new InvalidOperationException("The order of this payment no longer exists");

        if (!string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Order is not in Paid state");

        var changedBy = await _currentUser.ResolveExistingActorAsync(
            _db, request.CreatedBy, _logger, nameof(request.CreatedBy), ct);

        // Giao dịch MoMo thật: phải gọi API hoàn của MoMo trước khi ghi nhận.
        if (string.Equals(payment.Provider, PaymentProviders.MoMo, StringComparison.OrdinalIgnoreCase)
            && !_momo.IsMockMode)
        {
            var payload = _momo.BuildRefundPayload(
                order.OrderId.ToString(),
                payment.ProviderTransactionId ?? string.Empty,
                payment.Amount,
                request.Reason.Trim(),
                Guid.NewGuid().ToString("N"),
                payment.PaymentId.ToString());

            var result = await _momo.RefundAsync(payload, ct);
            if (!result.Success)
            {
                payment.Note = result.Error;
                await _db.SaveChangesAsync(ct);
                throw new InvalidOperationException($"MoMo refund failed: {result.Error}");
            }
        }

        payment.Status = PaymentStatuses.Refunded;
        payment.RefundedAt = DateTime.UtcNow;
        payment.Note = request.Reason.Trim();

        // Đơn mở lại để thu lại (đơn đã phục vụ xong nên về Ready = chờ thu).
        _lifecycle.ApplyRefunded(order, changedBy, $"Refunded: {request.Reason.Trim()}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _tables.RefreshStatusForTableAsync(order.TableId, ct);
        return PaymentResponse.From(payment);
    }

    // ==========================================================
    // Đọc
    // ==========================================================

    public Task<List<Payment>> GetByOrderAsync(int orderId, CancellationToken ct) => _db.Payments.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);

    /// <summary>
    /// Danh sách thanh toán có lọc + phân trang cho <c>GET /api/payments</c> (P1).
    /// Lọc theo <c>PaidAt</c> khi có <c>from</c>/<c>to</c>.
    /// </summary>
    public async Task<PagedResponse<PaymentResponse>> GetListAsync(PaymentListQuery query, CancellationToken ct = default)
    {
        var skip = Paging.ResolveSkip(query.Page, query.PageSize);
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToLowerInvariant();
        var method = string.IsNullOrWhiteSpace(query.Method) ? null : query.Method.Trim().ToLowerInvariant();
        var orderId = query.OrderId;
        var from = query.From;
        var to = query.To;

        var filtered = _db.Payments.AsNoTracking()
            .Where(x => (status == null || x.Status.ToLower() == status)
                     && (method == null || x.PaymentMethod.ToLower() == method)
                     && (orderId == null || x.OrderId == orderId)
                     && (from == null || x.PaidAt >= from)
                     && (to == null || x.PaidAt <= to))
            .Select(x => new PaymentResponse
            {
                PaymentId = x.PaymentId,
                OrderId = x.OrderId,
                Amount = x.Amount,
                PaymentMethod = x.PaymentMethod,
                Status = x.Status,
                TransactionCode = x.TransactionCode,
                PaidAt = x.PaidAt,
                CreatedBy = x.CreatedBy,
                Provider = x.Provider,
                RequestId = x.RequestId,
                ProviderTransactionId = x.ProviderTransactionId,
                Note = x.Note,
                ProviderResponse = x.ProviderResponse,
                CreatedAt = x.CreatedAt,
                RefundedAt = x.RefundedAt
            });

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(x => x.PaidAt)
            .ThenByDescending(x => x.PaymentId)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResponse<PaymentResponse>
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total,
            Items = items
        };
    }

    // ==========================================================
    // Helper
    // ==========================================================

    /// <summary>URL giả lập (chế độ Mock) — FE mở URL này để "đóng tiền" trong test.</summary>
    private string BuildMockRedirectUrl(int paymentId)
    {
        var path = $"/api/payments/momo/mock/{paymentId}";
        var baseUrl = _options.MockRedirectBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl)
            ? path
            : $"{baseUrl.TrimEnd('/')}{path}";
    }

    /// <summary>Chuyển bản ghi payment thành response của intent MoMo.</summary>
    private MomoIntentResponse ToIntentResponse(Payment payment)
    {
        var redirectUrl = string.Empty;
        var qrCodeUrl = string.Empty;

        if (!string.IsNullOrWhiteSpace(payment.ProviderResponse))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<MomoPaymentService.MomoCreateResponse>(
                    payment.ProviderResponse,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                redirectUrl = parsed?.RedirectUrl ?? string.Empty;
                qrCodeUrl = parsed?.QrCodeUrl ?? string.Empty;
            }
            catch (JsonException)
            {
                // Body lưu không đọc được ⇒ không có URL để trả về; không làm hỏng request.
            }
        }

        return new MomoIntentResponse
        {
            PaymentId = payment.PaymentId,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Status = payment.Status,
            Provider = payment.Provider,
            RequestId = payment.RequestId ?? string.Empty,
            RedirectUrl = redirectUrl,
            QrCodeUrl = qrCodeUrl,
            IsMock = _momo.IsMockMode
        };
    }

    /// <summary>Kết quả xử lý IPN.</summary>
    public sealed record MomoNotificationOutcome(
        bool Accepted,
        bool WasApplied,
        int PaymentId,
        string Message)
    {
        public static MomoNotificationOutcome Applied(int paymentId) =>
            new(true, true, paymentId, "Payment applied");

        public static MomoNotificationOutcome AlreadyApplied(int paymentId) =>
            new(true, false, paymentId, "Payment was already applied");

        public static MomoNotificationOutcome Failed(int paymentId, string message) =>
            new(true, false, paymentId, message);

        public static MomoNotificationOutcome Rejected(string message) =>
            new(false, false, 0, message);
    }
}
