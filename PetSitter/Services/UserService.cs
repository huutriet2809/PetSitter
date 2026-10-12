using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Utils;

namespace PetSitter.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;

        public UserService(IUserRepository userRepository, ITokenService tokenService)
        {
            _userRepository = userRepository;
            _tokenService = tokenService;
        }

        public async Task<UserDto?> AuthenticateAsync(LoginDto loginDto)
        {
            var user = await _userRepository.GetUserByEmailAsync(loginDto.Email);

            if (user == null || !PasswordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                return null;
            }

            var token = _tokenService.GenerateToken(user);

            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                Token = token
            };
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            if (await _userRepository.UserExistsAsync(registerDto.Email))
            {
                return null; // or throw custom exception
            }

            var user = new User
            {
                FullName = registerDto.FullName,
                Email = registerDto.Email,
                Phone = registerDto.Phone,
                PasswordHash = PasswordHasher.HashPassword(registerDto.Password),
                Role = "USER",
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var token = _tokenService.GenerateToken(user);

            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                Token = token
            };
        }

        public async Task<User?> GetUserByIdAsync(string id)
        {
            return await _userRepository.GetByIdAsync(id);
        }

        public async Task<UserDto> UpdateProfileAsync(string id, UpdateProfileDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length < 2 || dto.Name.Length > 60)
                throw new BadRequestException("Tên phải từ 2 đến 60 ký tự.");
            
            if (!string.IsNullOrEmpty(dto.Phone) && !System.Text.RegularExpressions.Regex.IsMatch(dto.Phone, @"^0\d{9}$"))
                throw new BadRequestException("Số điện thoại không hợp lệ (Bắt đầu bằng 0 và gồm 10 số).");

            if (!string.IsNullOrEmpty(dto.AvatarUrl) && (!dto.AvatarUrl.StartsWith("https://") || dto.AvatarUrl.Length > 500))
                throw new BadRequestException("Ảnh đại diện không hợp lệ.");

            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) throw new NotFoundException("Không tìm thấy người dùng.");

            user.FullName = dto.Name;
            user.Phone = dto.Phone;
            user.AvatarUrl = dto.AvatarUrl;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl
            };
        }

        public async Task ChangePasswordAsync(string id, ChangePasswordDto dto)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) throw new NotFoundException("Không tìm thấy người dùng.");

            if (!PasswordHasher.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
                throw new BadRequestException("Mật khẩu hiện tại không đúng.");

            if (dto.NewPassword.Length < 8)
                throw new BadRequestException("Mật khẩu mới phải có ít nhất 8 ký tự.");

            if (dto.CurrentPassword == dto.NewPassword)
                throw new BadRequestException("Mật khẩu mới phải khác mật khẩu hiện tại.");

            user.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }
    }
}