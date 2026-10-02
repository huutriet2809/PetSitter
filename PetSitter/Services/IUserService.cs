using PetSitter.DTOs;
using PetSitter.Models;

namespace PetSitter.Services
{
    public interface IUserService
    {
        Task<UserDto?> AuthenticateAsync(LoginDto loginDto);
        Task<UserDto?> RegisterAsync(RegisterDto registerDto);
        Task<User?> GetUserByIdAsync(int id);
    }
}
