using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PetSitter.Helpers;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class JwtKeyRotationTests
    {
        private const string InitialSecret = "ThisIsASecretKeyForTestingJwtRotation_1234567890";
        private const string RotatedSecret = "ThisIsANewRotatedKeyForJwtTesting_987654321000";
        private const string ThirdSecret = "ThisIsAThirdSecretKeyForTestingJwtRotation_9999";

        [Fact]
        public void ValidateKey_ShortKeyLessThan32Bytes_ThrowsInvalidOperationException()
        {
            var shortSecret = "ShortKey123"; // < 32 bytes
            Assert.Throws<InvalidOperationException>(() => JwtKeyHelper.ValidateKey(shortSecret));
        }

        [Fact]
        public void GenerateAccessToken_IncludesKidInHeader()
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = InitialSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

            var tokenService = new TokenService(config, null!, null!);
            var user = new User
            {
                UserId = "user123",
                Email = "test@pawstay.vn",
                FullName = "Test User",
                Role = "User"
            };

            var tokenString = tokenService.GenerateAccessToken(user);
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenString);

            Assert.NotNull(jwt.Header.Kid);
            var expectedKid = JwtKeyHelper.ComputeKeyId(InitialSecret);
            Assert.Equal(expectedKid, jwt.Header.Kid);
        }

        [Fact]
        public void KeyRotation_OldTokenVerifiedByPreviousSecrets_Success()
        {
            // 1. Tạo token bằng InitialSecret
            var configV1 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = InitialSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            }).Build();

            var tokenServiceV1 = new TokenService(configV1, null!, null!);
            var user = new User { UserId = "user123", Email = "test@pawstay.vn", FullName = "Test User", Role = "User" };
            var oldTokenString = tokenServiceV1.GenerateAccessToken(user);

            // 2. Xoay key: RotatedSecret trở thành active key, InitialSecret chuyển vào PreviousSecrets
            var configV2 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = RotatedSecret,
                ["JwtSettings:PreviousSecrets:0"] = InitialSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            }).Build();

            var validationKeys = JwtKeyHelper.GetAllValidationKeys(configV2);
            var handler = new JwtSecurityTokenHandler();

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "PetSitterApp",
                ValidAudience = "PetSitterUsers",
                IssuerSigningKeys = validationKeys,
                IssuerSigningKeyResolver = (tokenString, securityToken, kid, valParams) =>
                {
                    if (!string.IsNullOrEmpty(kid))
                    {
                        var match = valParams.IssuerSigningKeys?.FirstOrDefault(k => k.KeyId == kid);
                        if (match != null) return new[] { match };
                    }
                    return valParams.IssuerSigningKeys ?? Enumerable.Empty<SecurityKey>();
                },
                ClockSkew = TimeSpan.FromSeconds(5)
            };

            // Verify old token thành công nhờ PreviousSecrets
            var principal = handler.ValidateToken(oldTokenString, validationParams, out var validatedToken);
            Assert.NotNull(principal);
            Assert.Equal("user123", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            // 3. Tạo token mới bằng key hiện tại
            var tokenServiceV2 = new TokenService(configV2, null!, null!);
            var newTokenString = tokenServiceV2.GenerateAccessToken(user);
            var newJwt = handler.ReadJwtToken(newTokenString);
            Assert.Equal(JwtKeyHelper.ComputeKeyId(RotatedSecret), newJwt.Header.Kid);

            var principalNew = handler.ValidateToken(newTokenString, validationParams, out _);
            Assert.NotNull(principalNew);
        }

        [Fact]
        public void KeyRotation_RemovedOldKey_RejectsToken()
        {
            // 1. Tạo token bằng InitialSecret
            var configV1 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = InitialSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            }).Build();

            var tokenService = new TokenService(configV1, null!, null!);
            var user = new User { UserId = "user123", Email = "test@pawstay.vn", FullName = "Test User", Role = "User" };
            var tokenString = tokenService.GenerateAccessToken(user);

            // 2. Hệ thống chỉ tin ThirdSecret và RotatedSecret (InitialSecret đã bị loại bỏ)
            var configV3 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = ThirdSecret,
                ["JwtSettings:PreviousSecrets:0"] = RotatedSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            }).Build();

            var validationKeys = JwtKeyHelper.GetAllValidationKeys(configV3);
            var handler = new JwtSecurityTokenHandler();

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "PetSitterApp",
                ValidAudience = "PetSitterUsers",
                IssuerSigningKeys = validationKeys,
                IssuerSigningKeyResolver = (tok, secTok, kid, valParams) =>
                {
                    if (!string.IsNullOrEmpty(kid))
                    {
                        var match = valParams.IssuerSigningKeys?.FirstOrDefault(k => k.KeyId == kid);
                        if (match != null) return new[] { match };
                    }
                    return valParams.IssuerSigningKeys ?? Enumerable.Empty<SecurityKey>();
                },
                ClockSkew = TimeSpan.FromSeconds(5)
            };

            // Token cũ bị từ chối
            Assert.ThrowsAny<SecurityTokenException>(() =>
                handler.ValidateToken(tokenString, validationParams, out _));
        }

        [Fact]
        public void ValidateToken_TamperedSignature_ThrowsSecurityTokenException()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = InitialSecret,
                ["JwtSettings:Issuer"] = "PetSitterApp",
                ["JwtSettings:Audience"] = "PetSitterUsers"
            }).Build();

            var tokenService = new TokenService(config, null!, null!);
            var user = new User { UserId = "user123", Email = "test@pawstay.vn", FullName = "Test User", Role = "User" };
            var tokenString = tokenService.GenerateAccessToken(user);

            // Làm giả chữ ký bằng cách thay đổi ký tự cuối
            var parts = tokenString.Split('.');
            var tamperedSignature = parts[2].Substring(0, parts[2].Length - 4) + "AAAA";
            var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

            var handler = new JwtSecurityTokenHandler();
            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "PetSitterApp",
                ValidAudience = "PetSitterUsers",
                IssuerSigningKeys = JwtKeyHelper.GetAllValidationKeys(config),
                ClockSkew = TimeSpan.FromSeconds(5)
            };

            Assert.ThrowsAny<SecurityTokenException>(() =>
                handler.ValidateToken(tamperedToken, validationParams, out _));
        }
    }
}

