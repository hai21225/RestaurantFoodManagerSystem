public class AuthService
{
    private readonly UserService _userService;
    private readonly UserRoleService _userRoleService;
    private readonly RoleService _roleService;
    public AuthService(UserService userService, UserRoleService userRoleService,RoleService roleService)
    {
        _userService = userService;
        _userRoleService = userRoleService;
        _roleService = roleService;
    }

    public async Task<UserDto?> Login(string username, string password)
    {
        var users = await _userService.FindUserByUsernameAsync(username);

        if (users == null || users.IsActive != true || string.IsNullOrWhiteSpace(users.Password))
        {
            return null;
        }

        var result = BCrypt.Net.BCrypt.Verify(password, users.Password);
        
        if(!result)
        {
            return null;
        }

        return users;
    }

    public async Task<bool> Register(UserDto userDto)
    {
        var user = await _userService.FindUserByUsernameAsync(userDto.UserName ?? "");
        if (user != null)
        {
            throw new InvalidOperationException("Username already exists");
        }

        if (string.IsNullOrWhiteSpace(userDto.UserName) ||
            string.IsNullOrWhiteSpace(userDto.Email) ||
            string.IsNullOrWhiteSpace(userDto.Password))
        {
            throw new ArgumentException("Username, email and password are required");
        }

        if(userDto.Password != userDto.ConfirmPassword)
        {
            return false;
        }
        var userdto = new UserDto
        {
            UserName = userDto.UserName,
            Email = userDto.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(userDto.Password),
            FullName = userDto.FullName,
            Phone = userDto.Phone,
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        return await _userService.AddUserAsync(userdto);
    }

    public async Task<bool> AssignRoleToUser(int userid, string rolename)
    {

        var role = await _roleService.GetRoleByRoleNameAsync(rolename); 


        if (role == null)
        {
            throw new Exception("Role not found");
        }
        var result=await _userRoleService.AddUserRoleAsync(new UserRoleDto
        {
            UserId = userid,
            RoleId = role.RoleId,
        });

        return result;
    }

}
