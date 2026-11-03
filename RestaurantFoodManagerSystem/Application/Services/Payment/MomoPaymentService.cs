using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hiện thực giao thức MoMo Payment Gateway (Đợt 3).
///
/// <para>
/// Chia 2 lớp rõ ràng:
/// <list type="bullet">
/// <item><description><b>Lớp giao thức</b> (class này): dựng canonical string, ký/kiểm tra
/// HMAC-SHA256, dựng payload create/notify/refund. <b>Không</b> gọi mạng ở đây.</description></item>
/// <item><description><b>Lớp nghiệp vụ</b> (<see cref="PaymentService"/>): quyết định khi nào
/// tạo intent, khi nào đánh dấu đã thu, kiểm tra số tiền, idempotency.</description></item>
/// </list>
///
/// <para>
/// <b>Chế độ giả lập (<c>Mock</c>):</b> không gọi ra ngoài. Backend tự sinh payload IPN
/// <b>đã ký chữ ký đúng</b> bằng đúng khoá mà lúc verify dùng ⇒ chạy đúng đường kiểm tra
/// thật (sai chữ ký / sai số tiền / trùng IPN đều bị bắt như ngoài đời).
/// </para>
/// </summary>
public sealed class MomoPaymentService
{
    /// <summary>Khoá giả lập khi chưa cấu hình khoá MoMo thật (chỉ dùng ở chế độ Mock).</summary>
    private const string MockPartnerCode = "MOCK_PARTNER";
    private const string MockAccessKey = "MOCK_ACCESS_KEY";
    private const string MockSecretKey = "MOCK_SECRET_KEY";

    /// <summary>Result code của MoMo = thanh toán thành công.</summary>
    public const string SuccessResultCode = "9000";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PaymentOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MomoPaymentService> _logger;

    public MomoPaymentService(
        PaymentOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<MomoPaymentService> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary><c>true</c> khi đang ở chế độ giả lập (không gọi ra ngoài).</summary>
    public bool IsMockMode =>
        !string.Equals(_options.Provider, PaymentProviders.MoMo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>true</c> khi đã có đủ 3 khoá MoMo thật. Nếu cấu hình <c>Provider = MoMo</c> mà
    /// thiếu khoá thì không được gọi ra ngoài — phải báo lỗi cấu hình rõ ràng.
    /// </summary>
    public bool HasRealCredentials =>
        !string.IsNullOrWhiteSpace(_options.MoMo.PartnerCode)
        && !string.IsNullOrWhiteSpace(_options.MoMo.AccessKey)
        && !string.IsNullOrWhiteSpace(_options.MoMo.SecretKey);

    private string PartnerCode => Value(_options.MoMo.PartnerCode, MockPartnerCode);
    private string AccessKey => Value(_options.MoMo.AccessKey, MockAccessKey);
    private string SecretKey => Value(_options.MoMo.SecretKey, MockSecretKey);

    private static string Value(string configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    // ==========================================================
    // Canonical string + chữ ký
    // ==========================================================

    /// <summary>
    /// Canonical string của MoMo: các tham số nối bằng <c>&amp;</c>, **đúng thứ tự** và
    /// **đúng tên** mà MoMo quy định. Sai thứ tự ⇒ chữ ký không khớp ⇒ từ chối giao dịch.
    /// </summary>
    public string BuildCanonical(
        string amount,
        string extraData,
        string ipAddress,
        string orderId,
        string orderInfo,
        string partnerCode,
        string partnerName,
        string partnerRefCode,
        string redirectUrl,
        string requestId,
        string requestType,
        string notificationUrl) =>
        $"accessKey={AccessKey}" +
        $"&amount={amount}" +
        $"&extraData={extraData}" +
        $"&ipAddress={ipAddress}" +
        $"&orderId={orderId}" +
        $"&orderInfo={orderInfo}" +
        $"&partnerCode={partnerCode}" +
        $"&partnerName={partnerName}" +
        $"&partnerRefCode={partnerRefCode}" +
        $"&redirectUrl={redirectUrl}" +
        $"&requestId={requestId}" +
        $"&requestType={requestType}" +
        $"&notificationUrl={notificationUrl}";

    /// <summary>HMAC-SHA256(secretKey, canonical) → chữ ký hex lowercase.</summary>
    public string Sign(string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// So chữ ký theo thời gian hằng số (tránh rò rỉ thông tin qua thời gian phản hồi).
    /// </summary>
    public bool VerifySignature(string canonical, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Sign(canonical));
        var actual = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // ==========================================================
    // Create payment
    // ==========================================================

    /// <summary>Dựng payload tạo giao dịch (đã ký chữ ký).</summary>
    public MomoCreatePayload BuildCreatePayload(
        decimal amount,
        string orderId,
        string orderInfo,
        string requestId,
        string partnerRefCode,
        string extraData)
    {
        var o = _options.MoMo;
        var amountText = FormatAmount(amount);

        var canonical = BuildCanonical(
            amountText,
            extraData,
            o.IpAddress,
            orderId,
            orderInfo,
            PartnerCode,
            o.PartnerName,
            partnerRefCode,
            o.RedirectUrl,
            requestId,
            "capture",
            o.NotificationUrl);

        return new MomoCreatePayload
        {
            PartnerCode = PartnerCode,
            PartnerName = o.PartnerName,
            PartnerRefCode = partnerRefCode,
            Amount = amountText,
            OrderId = orderId,
            OrderInfo = orderInfo,
            RedirectUrl = o.RedirectUrl,
            IpAddress = o.IpAddress,
            ExtraData = extraData,
            RequestId = requestId,
            RequestType = "capture",
            NotificationUrl = o.NotificationUrl,
            Signature = Sign(canonical)
        };
    }

    /// <summary>
    /// Gọi API tạo giao dịch của MoMo. Chỉ dùng ở chế độ thật — chế độ giả lập không gọi mạng.
    /// </summary>
    public async Task<MomoCreateResult> CreateAsync(
        MomoCreatePayload payload,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(MomoPaymentService));

        using var response = await client.PostAsJsonAsync(
            $"{_options.MoMo.ApiBaseUrl.TrimEnd('/')}/create",
            payload,
            Json,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "MoMo create failed: {Status} {Body}",
                (int)response.StatusCode,
                body);
            return MomoCreateResult.Failed(body);
        }

        MomoCreateResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MomoCreateResponse>(body, Json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "MoMo create returned a non-JSON body");
            return MomoCreateResult.Failed("MoMo returned an unreadable response");
        }

        if (parsed is null || parsed.ResponseCode != 0 || string.IsNullOrWhiteSpace(parsed.RedirectUrl))
        {
            return MomoCreateResult.Failed(parsed?.Message ?? body);
        }

        return MomoCreateResult.Succeeded(
            parsed.RedirectUrl!,
            parsed.QrCodeUrl ?? string.Empty,
            parsed.TransId ?? string.Empty,
            body);
    }

    // ==========================================================
    // Notification (IPN)
    // ==========================================================

    /// <summary>
    /// Dựng lại canonical string từ payload IPN để kiểm tra chữ ký.
    /// Các giá trị không có trong IPN (<c>accessKey</c>, <c>ipAddress</c>, <c>partnerName</c>,
    /// <c>redirectUrl</c>, <c>notificationUrl</c>) lấy từ cấu hình — đúng những giá trị đã
    /// gửi lúc tạo giao dịch.
    /// </summary>
    public string BuildNotificationCanonical(MomoNotificationRequest payload)
    {
        var o = _options.MoMo;
        return BuildCanonical(
            payload.Amount ?? string.Empty,
            payload.ExtraData ?? string.Empty,
            o.IpAddress,
            payload.OrderId ?? string.Empty,
            payload.OrderInfo ?? string.Empty,
            payload.PartnerCode ?? string.Empty,
            o.PartnerName,
            payload.PartnerRefCode ?? string.Empty,
            o.RedirectUrl,
            payload.RequestId ?? string.Empty,
            payload.RequestType ?? string.Empty,
            o.NotificationUrl);
    }

    /// <summary>Kiểm tra chữ ký của payload IPN.</summary>
    public bool VerifyNotification(MomoNotificationRequest payload) =>
        VerifySignature(BuildNotificationCanonical(payload), payload.Signature);

    /// <summary>
    /// Sinh payload IPN **đã ký đúng** để giả lập thanh toán ở môi trường test.
    /// Payload này đi qua <b>đúng</b> đường verify của IPN thật.
    /// </summary>
    public MomoNotificationRequest BuildMockNotification(
        MomoCreatePayload create,
        bool success,
        string message)
    {
        var o = _options.MoMo;
        var resultCode = success ? SuccessResultCode : "4001";

        var canonical = BuildCanonical(
            create.Amount,
            create.ExtraData,
            o.IpAddress,
            create.OrderId,
            create.OrderInfo,
            create.PartnerCode,
            o.PartnerName,
            create.PartnerRefCode,
            o.RedirectUrl,
            create.RequestId,
            create.RequestType,
            o.NotificationUrl);

        return new MomoNotificationRequest
        {
            PartnerCode = create.PartnerCode,
            PartnerRefCode = create.PartnerRefCode,
            Amount = create.Amount,
            OrderId = create.OrderId,
            OrderInfo = create.OrderInfo,
            RequestId = create.RequestId,
            RequestType = create.RequestType,
            NotificationUrl = o.NotificationUrl,
            ResultCode = resultCode,
            ResponseCode = resultCode,
            ExtraData = create.ExtraData,
            Message = message,
            TransId = success ? $"MOCK-{Guid.NewGuid():N}" : string.Empty,
            ResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Signature = Sign(canonical)
        };
    }

    // ==========================================================
    // Refund
    // ==========================================================

    /// <summary>Dựng payload hoàn tiền (đã ký chữ ký).</summary>
    public MomoRefundPayload BuildRefundPayload(
        string orderId,
        string transId,
        decimal amount,
        string reason,
        string requestId,
        string extraData)
    {
        var amountText = FormatAmount(amount);

        // Canonical string của API refund theo spec MoMo.
        var canonical =
            $"accessKey={AccessKey}" +
            $"&amount={amountText}" +
            $"&extraData={extraData}" +
            $"&orderId={orderId}" +
            $"&partnerCode={PartnerCode}" +
            $"&reason={reason}" +
            $"&requestId={requestId}" +
            $"&transId={transId}";

        return new MomoRefundPayload
        {
            PartnerCode = PartnerCode,
            RequestId = requestId,
            OrderId = orderId,
            TransId = transId,
            Amount = amountText,
            Reason = reason,
            ExtraData = extraData,
            Signature = Sign(canonical)
        };
    }

    /// <summary>Gọi API hoàn tiền của MoMo (chỉ dùng ở chế độ thật).</summary>
    public async Task<MomoRefundResult> RefundAsync(
        MomoRefundPayload payload,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(MomoPaymentService));

        using var response = await client.PostAsJsonAsync(
            $"{_options.MoMo.ApiBaseUrl.TrimEnd('/')}/refund",
            payload,
            Json,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("MoMo refund failed: {Status} {Body}", (int)response.StatusCode, body);
            return MomoRefundResult.Failed(body);
        }

        MomoRefundResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MomoRefundResponse>(body, Json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "MoMo refund returned a non-JSON body");
            return MomoRefundResult.Failed("MoMo returned an unreadable response");
        }

        if (parsed is null || parsed.ResponseCode != 0)
        {
            return MomoRefundResult.Failed(parsed?.Message ?? body);
        }

        return MomoRefundResult.Succeeded(parsed.TransId ?? string.Empty, body);
    }

    /// <summary>
    /// MoMo dùng số nguyên (đồng) cho số tiền. làm tròn về số nguyên gần nhất, bỏ phần lẻ.
    /// </summary>
    public static string FormatAmount(decimal amount) =>
        ((long)Math.Round(amount, 0, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Nội dung đơn hàng gửi lên MoMo. Dùng chung 1 hàm để lúc tạo intent và lúc dựng
    /// payload giả lập/verify luôn khớp nhau (orderInfo nằm trong canonical string).
    /// </summary>
    public static string BuildOrderInfo(int orderId) => $"Thanh toan don {orderId}";

    // ==========================================================
    // Payload / kết quả (kiểu lồng để 1 file = 1 lớp chính)
    // ==========================================================

    /// <summary>Body gửi lên MoMo khi tạo giao dịch.</summary>
    public sealed class MomoCreatePayload
    {
        public string PartnerCode { get; set; } = string.Empty;
        public string PartnerName { get; set; } = string.Empty;
        public string PartnerRefCode { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string OrderInfo { get; set; } = string.Empty;
        public string RedirectUrl { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string ExtraData { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string RequestType { get; set; } = "capture";
        public string NotificationUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>Body MoMo trả về khi tạo giao dịch.</summary>
    public sealed class MomoCreateResponse
    {
        public string? PartnerCode { get; set; }
        public string? PartnerRefCode { get; set; }
        public string? Amount { get; set; }
        public string? OrderId { get; set; }
        public string? OrderInfo { get; set; }
        public string? RedirectUrl { get; set; }
        public string? QrCodeUrl { get; set; }
        public string? RequestId { get; set; }
        public string? RequestType { get; set; }
        public string? NotificationUrl { get; set; }
        public string? TransId { get; set; }
        public string? Message { get; set; }
        public int ResponseCode { get; set; }
        public int ErrorCode { get; set; }
    }

    /// <summary>Body gửi lên MoMo khi hoàn tiền.</summary>
    public sealed class MomoRefundPayload
    {
        public string PartnerCode { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string TransId { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ExtraData { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>Body MoMo trả về khi hoàn tiền.</summary>
    public sealed class MomoRefundResponse
    {
        public string? PartnerCode { get; set; }
        public string? RequestId { get; set; }
        public string? OrderId { get; set; }
        public string? TransId { get; set; }
        public string? Amount { get; set; }
        public string? Message { get; set; }
        public int ResponseCode { get; set; }
    }

    /// <summary>Kết quả tạo giao dịch đã quy về dạng dùng chung.</summary>
    public sealed record MomoCreateResult(
        bool Success,
        string RedirectUrl,
        string QrCodeUrl,
        string TransId,
        string RawResponse,
        string Error)
    {
        public static MomoCreateResult Succeeded(string redirectUrl, string qrCodeUrl, string transId, string raw) =>
            new(true, redirectUrl, qrCodeUrl, transId, raw, string.Empty);

        public static MomoCreateResult Failed(string error) =>
            new(false, string.Empty, string.Empty, string.Empty, string.Empty, error);
    }

    /// <summary>Kết quả hoàn tiền đã quy về dạng dùng chung.</summary>
    public sealed record MomoRefundResult(bool Success, string TransId, string RawResponse, string Error)
    {
        public static MomoRefundResult Succeeded(string transId, string raw) =>
            new(true, transId, raw, string.Empty);

        public static MomoRefundResult Failed(string error) =>
            new(false, string.Empty, string.Empty, error);
    }
}
