using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class EarningsAccount
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AccountId { get; set; } = null!;

        public string UserId { get; set; } = null!; // Unique Index

        public long ReservedAmount { get; set; } = 0; // Tiền đang tạm giữ cho các yêu cầu rút mở

        public long DailyWithdrawn { get; set; } = 0; // Tiền đã rút trong ngày hiện tại (giờ VN)

        public string DailyWindowDate { get; set; } = string.Empty; // "yyyy-MM-dd" giờ VN (UTC+7)

        public long Version { get; set; } = 1; // Khóa lạc quan chống race condition

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
