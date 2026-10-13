using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PetSitter.Models
{
    public class RefreshToken
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 hash của chuỗi token ngẫu nhiên 256-bit. Token thô không bao giờ được lưu trực tiếp trong DB.
        /// </summary>
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>
        /// Thời điểm hết hạn của refresh token (7 ngày kể từ khi tạo).
        /// GHI CHÚ: TTL index của MongoDB được đặt trên trường ExpiresAt này (ExpireAfter = 0).
        /// TUYỆT ĐỐI KHÔNG dùng RevokedAt làm TTL vì hệ thống cần giữ lại các bản ghi đã revoke cho đến khi hết hạn
        /// để có thể phát hiện hành vi tái sử dụng token (Token Reuse / Replay Attack).
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Thời điểm token bị thu hồi.
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>
        /// TokenHash của token mới thay thế cho token này trong quá trình token rotation.
        /// </summary>
        public string? ReplacedBy { get; set; }

        public string? CreatedByIp { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
