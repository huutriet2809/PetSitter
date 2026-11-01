using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace PetSitter.Helpers
{
    public static class JwtKeyHelper
    {
        public const int MinimumKeySizeBytes = 32; // 256 bits

        public static string ComputeKeyId(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return string.Empty;
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hashBytes).Substring(0, 16).ToLowerInvariant();
        }

        public static void ValidateKey(string? secret, string keyName = "JwtSettings:Secret")
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Lỗi cấu hình JWT: Khóa '{keyName}' bị rỗng hoặc đang dùng giá trị mẫu. Vui lòng thiết lập khóa bí mật an toàn qua user-secrets hoặc biến môi trường.");
            }

            var byteCount = Encoding.UTF8.GetByteCount(secret);
            if (byteCount < MinimumKeySizeBytes)
            {
                throw new InvalidOperationException($"Lỗi cấu hình JWT: Khóa '{keyName}' có độ dài {byteCount} bytes, không đạt tiêu chuẩn tối thiểu {MinimumKeySizeBytes} bytes (256 bits).");
            }
        }

        public static SymmetricSecurityKey GetActiveSigningKey(IConfiguration config)
        {
            var secret = config["JwtSettings:Secret"];
            ValidateKey(secret, "JwtSettings:Secret");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!))
            {
                KeyId = ComputeKeyId(secret!)
            };
            return key;
        }

        public static List<SecurityKey> GetAllValidationKeys(IConfiguration config)
        {
            var keys = new List<SecurityKey>();

            // 1. Khóa ký hiện tại (Active Key)
            var activeSecret = config["JwtSettings:Secret"];
            if (!string.IsNullOrWhiteSpace(activeSecret) && !activeSecret.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                ValidateKey(activeSecret, "JwtSettings:Secret");
                keys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(activeSecret))
                {
                    KeyId = ComputeKeyId(activeSecret)
                });
            }

            // 2. Danh sách các khóa cũ dùng để verify khi xoay vòng khóa (PreviousSecrets)
            var prevSecrets = config.GetSection("JwtSettings:PreviousSecrets").Get<string[]>();
            if (prevSecrets != null)
            {
                for (int i = 0; i < prevSecrets.Length; i++)
                {
                    var prevSecret = prevSecrets[i];
                    if (string.IsNullOrWhiteSpace(prevSecret) || prevSecret.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var byteCount = Encoding.UTF8.GetByteCount(prevSecret);
                    if (byteCount >= MinimumKeySizeBytes)
                    {
                        keys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(prevSecret))
                        {
                            KeyId = ComputeKeyId(prevSecret)
                        });
                    }
                }
            }

            if (keys.Count == 0)
            {
                throw new InvalidOperationException("CRITICAL: Không có bất kỳ khóa ký JWT hợp lệ nào được cấu hình!");
            }

            return keys;
        }
    }
}

