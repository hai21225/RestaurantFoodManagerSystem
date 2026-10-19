public class LoginResponse
{
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }

    // Thêm ở GĐ6 (chỉ thêm field, không phá JSON cũ).
    public List<string> Roles { get; set; } = new();
    public string? Token { get; set; }
}