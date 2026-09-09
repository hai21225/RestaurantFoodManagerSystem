

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

    public async Task<bool> Login(string username, string password)
    {
        var users = await _userService.FindUserByUsernameAsync(username);

        if (users == null)
        {
            return false;
        }

        var result = BCrypt.Net.BCrypt.Verify(password, users.Password);
        
        return result;
    }

    public async Task<bool> Register(UserDto userDto)
    {
        var user = await _userService.FindUserByUsernameAsync(userDto.UserName??"");
        if (user != null)
        {
            throw new Exception("Username already exists");
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

        await _userService.AddUserAsync(userdto);

        return true;
    }

    public async Task<bool> UserPermissionAssignment(int userid, string rolename)
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