using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDto?> FindUserByUsernameAsync(string username)
    {
        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return null;
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == normalizedUsername);

        if (user == null)
        {
            return null;
        }
        var userDto = new UserDto
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.UserEmail,
            Password = user.UserPassword,
            FullName = user.UserFullName,
            Phone = user.UserPhone,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
        return userDto;
    }

    public async Task<bool> AddUserAsync(UserDto userdto)
    {
        var username = userdto.UserName?.Trim();
        var email = userdto.Email?.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Username and email are required");
        }

        if (await _db.Users.AnyAsync(x => x.UserName == username))
        {
            throw new InvalidOperationException("Username already exists");
        }

        if (await _db.Users.AnyAsync(x => x.UserEmail == email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var user = new Users
        {
            UserName = username,
            UserPassword = userdto.Password??"",
            UserFullName = userdto.FullName??"",
            UserEmail = email,
            UserPhone = userdto.Phone??"",
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await _db.Users.AddAsync(user);
        return await _db.SaveChangesAsync()>0;

    }
}
