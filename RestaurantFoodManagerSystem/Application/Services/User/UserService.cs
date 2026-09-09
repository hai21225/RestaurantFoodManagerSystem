public class UserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<Users> _userRepository;

    public UserService(IUnitOfWork unitOfWork, IRepository<Users> userRepository)
    {
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
    }

    public async Task<UserDto> GetUserByIdAsync(int id)
    {
        
        var user= await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new Exception("User not found");
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

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();
        var userDtos = users.Select(user => new UserDto
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
        }).ToList();
        return userDtos;
    }

    public async Task<UserDto> FindUserByUsernameAsync(string username)
    {
        var user = await _userRepository.FindOneAsync(u => u.UserName == username);
        if (user == null)
        {
            throw new Exception("User not found");
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
        var user = new Users
        {
            UserName = userdto.UserName??"",
            UserPassword = userdto.Password??"",
            UserFullName = userdto.FullName??"",
            UserEmail = userdto.Email??"",
            UserPhone = userdto.Phone??"",
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await _userRepository.AddAsync(user);
        return await _unitOfWork.SaveChangesAsync()>0;

    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new Exception("User not found");
        }
        _userRepository.Delete(user);
        return await _unitOfWork.SaveChangesAsync() > 0;

    }

    public async Task<bool> UpdateUserAsync(UserDto userdto)
    {
        var user = await _userRepository.GetByIdAsync(userdto.UserId);
        if (user == null)
        {
            throw new Exception("User not found");
        }
        user.UserName = userdto.UserName??user.UserName;
        user.UserPassword = userdto.Password??user.UserPassword;
        user.UserFullName = userdto.FullName??user.UserFullName;
        user.UserEmail = userdto.Email??user.UserEmail;
        user.UserPhone = userdto.Phone??user.UserPhone;
        user.IsActive = userdto.IsActive?? user.IsActive;
        user.UpdatedAt = DateTime.Now;
        _userRepository.Update(user);
        return await _unitOfWork.SaveChangesAsync() > 0;

    }
}