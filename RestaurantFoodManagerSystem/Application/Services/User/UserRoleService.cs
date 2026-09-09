public class UserRoleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<UserRoles> _userRoleRepository;
    
    public UserRoleService(IUnitOfWork unitOfWork, IRepository<UserRoles> userRoleRepository)
    {
        _unitOfWork = unitOfWork;
        _userRoleRepository = userRoleRepository;
    }

    public async Task<UserRoleDto> GetUserRoleByIdAsync(int id)
    {
        var userRole = await _userRoleRepository.GetByIdAsync(id);
        if (userRole == null)
        {
            throw new Exception("User role not found");
        }
        var userRoleDto = new UserRoleDto
        {
            RoleId = userRole.RoleId,
            UserId = userRole.UserId,
        };
        return userRoleDto;
    }

    public async Task<List<UserRoleDto>> GetAllUserRolesAsync()
    {
        var userRoles = await _userRoleRepository.GetAllAsync();
        var userRoleDtos = userRoles.Select(userRole => new UserRoleDto
        {
            RoleId = userRole.RoleId,
            UserId = userRole.UserId,
        }).ToList();
        return userRoleDtos;
    }

    public async Task<bool> AddUserRoleAsync(UserRoleDto userRoleDto)
    {
        if (userRoleDto == null)
        {
            throw new ArgumentNullException(nameof(userRoleDto));
        }
        var userRole = new UserRoles
        {
            RoleId = userRoleDto.RoleId,
            UserId = userRoleDto.UserId,
        };
        await _userRoleRepository.AddAsync(userRole);
        return await _unitOfWork.SaveChangesAsync()>0;
    }
}