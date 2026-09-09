public class RoleService
{
    private readonly IRepository<Roles> _roleRepository;

    public RoleService(IRepository<Roles> roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<Roles> GetRoleByRoleNameAsync(string name)
    {
        var role = await _roleRepository.FindOneAsync(x => x.RoleName == name);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        return role;
    }
}