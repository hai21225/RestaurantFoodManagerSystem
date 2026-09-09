public class Users
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public string UserPassword { get; set; }
    public string UserFullName { get; set; }
    public string UserEmail { get; set; }
    public string UserPhone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set;}

}