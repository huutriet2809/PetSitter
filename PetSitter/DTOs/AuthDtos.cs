using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class LoginDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? Phone { get; set; }
    }

    public class UserDto
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class UpdateProfileDto
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public class AdminUserDto
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Role { get; set; } = "USER";
        public bool IsActive { get; set; } = true;
        public DateTime? LockoutEndUtc { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateUserStatusDto
    {
        public bool IsActive { get; set; }
    }
    
    public class ResetPasswordDto
    {
        [Required]
        public string Token { get; set; } = string.Empty;
        [Required]
        [MinLength(8, ErrorMessage = "Mật khẩu mới tối thiểu 8 ký tự")]
        public string NewPassword { get; set; } = string.Empty;
    }
}