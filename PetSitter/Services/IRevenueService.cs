using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetSitter.DTOs;
using PetSitter.Models;

namespace PetSitter.Services
{
    public interface IRevenueService
    {
        Task<RevenueOverviewDto> GetRevenueOverviewAsync(DateTime nowUtc);
        Task<SweepItemDto> CreateRevenueSweepAsync(string adminUserId, CreateSweepDto dto, string? ipAddress, DateTime nowUtc);
        Task<List<SweepItemDto>> GetRevenueSweepsAsync(int limit = 50);

        Task<ReconciliationRun> RunReconciliationAsync(string adminUserId, ReconciliationRequestDto? requestDto, DateTime nowUtc);
        Task<List<ReconciliationRun>> GetReconciliationHistoryAsync(int limit = 30);

        Task<MonthlyReportDto> GetMonthlyReportAsync(int year, int month, DateTime nowUtc);
        Task<string> ExportMonthlyReportCsvAsync(int year, int month, DateTime nowUtc);
    }
}
