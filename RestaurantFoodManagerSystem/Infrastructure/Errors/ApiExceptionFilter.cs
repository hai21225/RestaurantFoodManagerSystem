using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RestaurantFoodManagerSystem.Infrastructure.Errors;

/// <summary>
/// Chuyển exception thành lỗi HTTP thống nhất cho **toàn bộ** API.
///
/// <para>
/// Trước đây mỗi controller tự `catch (Exception ex)` rồi trả `ex.Message`. Hệ quả:
/// lỗi hệ thống (lỗi SQL, mất kết nối…) cũng bị quy thành `400`/`404` với message thô
/// của tầng DB — vừa che lỗi thật, vừa có nguy cơ lộ thông tin nội bộ ra ngoài.
/// </para>
///
/// <para>
/// Nay service ném exception có ngữ nghĩa và filter này ánh xạ duy nhất:</para>
/// <list type="table">
/// <item><term><c>KeyNotFoundException</c></term><description><c>404</c> — không tìm thấy món/danhmục/bàn.</description></item>
/// <item><term><c>ArgumentException</c></term><description><c>400</c> — dữ liệu đầu vào sai.</description></item>
/// <item><term><c>InvalidOperationException</c></term><description><c>409</c> — xung đột nghiệp vụ (trùng tên, vi phạm luật).</description></item>
/// <item><term>khác</term><description><c>500</c> + message chung; chi tiết chỉ ghi log server-side.</description></item>
/// </list>
///
/// <para>Body lỗi luôn là <c>{ "message": "..." }</c> — đúng hợp đồng mà frontend đang dùng.</para>
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    /// <summary>Body lỗi chuẩn của toàn bộ API: <c>{ "message": "..." }</c>.</summary>
    public sealed record ErrorResponse(string Message);

    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;

        var (status, message) = exception switch
        {
            KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
            InvalidOperationException ex => (StatusCodes.Status409Conflict, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Lỗi lạ: ghi đầy đủ server-side, trả cho client message chung.
            _logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);
        }

        context.Result = new ObjectResult(new ErrorResponse(message))
        {
            StatusCode = status
        };
        context.ExceptionHandled = true;
    }
}
