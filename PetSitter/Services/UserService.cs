using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Utils;
using System;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly PetSitterContext _context;
        private readonly IConfiguration _configuration;

        public UserService(
            IUserRepository userRepository,
            ITokenService tokenService,
            PetSitterContext context,
            IConfiguration configuration)
        {
            _userRepository = userRepository;
            _tokenService = tokenService;
            _context = context;
            _configuration = configuration;
        }

        public async Task<UserDto?> AuthenticateAsync(LoginDto loginDto)
        {
            var user = await _userRepository.GetUserByEmailAsync(loginDto.Email);
            if (user == null)
            {
                return null;
            }

            var nowUtc = DateTime.UtcNow;

            // 1. Kiểm tra lockout tạm thời do nhập sai quá số lần
            if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > nowUtc)
            {
                var minutesRemaining = (int)Math.Ceiling((user.LockoutEndUtc.Value - nowUtc).TotalMinutes);
                throw new ForbiddenException($"Tài khoản tạm thời bị khóa vì lý do an toàn. Vui lòng thử lại sau {minutesRemaining} phút.");
            }

            // 2. Kiểm tra tài khoản bị vô hiệu hóa bởi Admin
            if (!user.IsActive)
            {
                throw new ForbiddenException("Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
            }

            // 3. Kiểm tra mật khẩu
            if (!PasswordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                // Cập nhật nguyên tử tăng số lần nhập sai ($inc)
                var maxAttempts = _configuration.GetValue<int>("AccountLockout:MaxFailedAttempts", 5);
                var lockoutMinutes = _configuration.GetValue<int>("AccountLockout:LockoutMinutes", 15);

                var userFilter = Builders<User>.Filter.Eq(u => u.UserId, user.UserId);
                var incUpdate = Builders<User>.Update.Inc(u => u.FailedLoginAttempts, 1);
                var updatedUser = await _context.Users.FindOneAndUpdateAsync(
                    userFilter,
                    incUpdate,
                    new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After }
                );

                if (updatedUser != null && updatedUser.FailedLoginAttempts >= maxAttempts)
                {
                    var lockEnd = nowUtc.AddMinutes(lockoutMinutes);
                    await _context.Users.UpdateOneAsync(
                        userFilter,
                        Builders<User>.Update
                            .Set(u => u.LockoutEndUtc, lockEnd)
                            .Set(u => u.FailedLoginAttempts, 0)
                    );
                    throw new ForbiddenException($"Tài khoản đã nhập sai mật khẩu quá {maxAttempts} lần và bị tạm khóa trong {lockoutMinutes} phút.");
                }

                return null;
            }

            // 4. Đăng nhập thành công -> Reset bộ đếm lỗi nguyên tử
            if (user.FailedLoginAttempts > 0 || user.LockoutEndUtc.HasValue)
            {
                await _context.Users.UpdateOneAsync(
                    Builders<User>.Filter.Eq(u => u.UserId, user.UserId),
                    Builders<User>.Update
                        .Set(u => u.FailedLoginAttempts, 0)
                        .Set(u => u.LockoutEndUtc, null)
                );
            }

            var token = _tokenService.GenerateToken(user);

            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl,
                Token = token
            };
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            if (await _userRepository.UserExistsAsync(registerDto.Email))
            {
                return null; // Email đã tồn tại
            }

            var user = new User
            {
                FullName = registerDto.FullName,
                Email = registerDto.Email,
                Phone = registerDto.Phone,
                PasswordHash = PasswordHasher.HashPassword(registerDto.Password),
                Role = "USER",
                IsActive = true,
                FailedLoginAttempts = 0,
                LockoutEndUtc = null,
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
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl,
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
            await _tokenService.RevokeAllUserTokensAsync(id);
        }
    }
}
