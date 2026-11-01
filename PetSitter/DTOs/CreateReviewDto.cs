using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class CreateReviewDto
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao")]
        public int Rating { get; set; }

        [MaxLength(500, ErrorMessage = "Nhận xét tối đa 500 ký tự")]
        public string? Comment { get; set; }
    }
}