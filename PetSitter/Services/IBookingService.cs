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
        Task ProcessLazyBookingUpdatesAsync(string? ownerId = null, string? sitterId = null, DateTime? nowUtc = null, bool forceReconcile = false);
        Task<BookingDetailDto> SitterCompleteBookingAsync(string currentUserId, string bookingId);
        Task<BookingDetailDto> OwnerConfirmCompletionAsync(string currentUserId, string bookingId);
        Task<BookingDetailDto> OwnerDisputeBookingAsync(string currentUserId, string bookingId, string lyDo);
        Task<BookingDetailDto> AdminResolveDisputeAsync(string adminUserId, string bookingId, ResolveDisputeDto dto);
        Task<List<BookingDetailDto>> GetDisputedBookingsForAdminAsync();
        Task<int> ReconcileMissingLedgerEntriesAsync(DateTime nowUtc, string? specificBookingId = null, int lookbackDays = 7, int limit = 100, bool force = false);
    }
}
