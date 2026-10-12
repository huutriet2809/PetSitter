using PetSitter.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface ISitterService
    {
        Task<List<SitterListDto>> GetSittersAsync(string? khuVuc, string? loaiPet, int page = 1, int pageSize = 10);
        Task<List<SitterListDto>> GetFeaturedSittersAsync();
        Task<SitterDetailDto?> GetSitterByIdAsync(string id);
        Task<List<ReviewDto>> GetSitterReviewsAsync(string id);
        Task<List<PublicReviewDto>> GetTopReviewsAsync(int limit = 6);
        Task<List<ServiceDto>> GetServicesAsync();
        Task<List<string>> GetSitterAvailabilityAsync(string id, string month);
                Task<StatsDto> GetStatsAsync();
        Task<SitterDetailDto?> GetSitterProfileByUserIdAsync(string userId);
        Task UpdateSitterProfileAsync(string userId, UpdateSitterProfileDto dto);
        Task<List<SitterDayAvailabilityDto>> GetSitterAvailabilityCalendarAsync(string userId, string month);
        Task UpdateSitterAvailabilityAsync(string userId, UpdateSitterAvailabilityDto dto);
    }
}
