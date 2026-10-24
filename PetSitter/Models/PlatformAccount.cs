using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class PlatformAccount
    {
        [BsonId]
        public string AccountId { get; set; } = "platform_main";

        public long SweptTotal { get; set; } = 0; // Tổng hoa hồng đã sweep về tài khoản vận hành

        public long PayosLinksCreatedCount { get; set; } = 0; // Số link PayOS đã tạo trong kỳ

        public long Version { get; set; } = 1; // Khóa lạc quan chống race condition

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class RevenueSweep
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SweepId { get; set; } = null!;

        public long Amount { get; set; }

        public string BankTransactionRef { get; set; } = null!; // Unique

        public string? Note { get; set; }

        public string AdminUserId { get; set; } = null!;

        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
