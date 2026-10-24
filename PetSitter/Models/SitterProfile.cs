using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterProfile
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        public int ExperienceYears { get; set; } = 0;

        [MaxLength(255)]
        public string? Address { get; set; }

        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Latitude { get; set; }

        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Longitude { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        public bool IsVerified { get; set; } = false;

        [Column(TypeName = "decimal(3, 2)")]
        public decimal Rating { get; set; } = 0;

        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        public string? LyDoTuChoi { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
        
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> LoaiPetNhan { get; set; } = new List<string>();

        public int TotalReviews { get; set; } = 0;

        // Tài khoản ngân hàng nhận tiền rút của Sitter
        public SitterBankAccount? BankAccount { get; set; }

        // Navigation properties
        [ForeignKey("UserId")]
        public User? User { get; set; }
        public ICollection<SitterService> SitterServices { get; set; } = new List<SitterService>();
        public ICollection<SitterAvailability> SitterAvailabilities { get; set; } = new List<SitterAvailability>();
    }

    public class SitterBankAccount
    {
        public string BankBin { get; set; } = null!; // Mã BIN / ngân hàng (VD: 970436, VCB, MB...)
        public string BankName { get; set; } = null!; // Tên ngân hàng
        public string EncryptedAccountNumber { get; set; } = null!; // Số tài khoản đã mã hóa Data Protection
        public string MaskedAccountNumber { get; set; } = null!; // ****1234
        public string AccountHolderName { get; set; } = null!; // Tên chủ tài khoản
        public bool NameMatchesRegisteredName { get; set; } = true; // Khớp với FullName đăng ký
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // Thời điểm cập nhật (để tính cooldown 24h)
    }
}

