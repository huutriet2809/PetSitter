using PetSitter.DTOs;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface IBookingService
    {
        Task<BookingDetailDto> CreateBookingAsync(string currentUserId, CreateBookingDto dto);
        Task<BookingDetailDto> GetBookingByIdAsync(string currentUserId, string bookingId);
        Task<System.Collections.Generic.List<BookingDetailDto>> GetMyBookingsAsync(string currentUserId);
        Task CancelBookingAsync(string currentUserId, string bookingId);
        Task<ReviewDto> AddReviewAsync(string currentUserId, string bookingId, CreateReviewDto dto);
    }
}
