public class Users
{
    public int Id { get; set; }
    public string UserName { get; set; }=string.Empty;
    public string UserPassword { get; set; }=string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; }=string.Empty;
    public string UserPhone { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set;}

}