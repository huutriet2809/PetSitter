using PetSitter.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface IBookingService
    {
        Task<BookingDetailDto> CreateBookingAsync(string currentUserId, CreateBookingDto dto);
        Task<BookingDetailDto> GetBookingByIdAsync(string currentUserId, string bookingId);
        Task<List<BookingDetailDto>> GetMyBookingsAsync(string currentUserId);
        Task CancelBookingAsync(string currentUserId, string bookingId, string? lyDo = null, string? expectedStatus = null);
        Task<ReviewDto> AddReviewAsync(string currentUserId, string bookingId, CreateReviewDto dto);
        Task ExpireOverduePendingBookingsAsync(string? ownerId = null, string? sitterId = null);
    }
}
