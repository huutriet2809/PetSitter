using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetSitter.DTOs;

namespace PetSitter.Services
{
    public interface IPaymentService
    {
        Task<PaymentLinkDto> CreateOrGetPaymentLinkAsync(string ownerUserId, string bookingId, DateTime nowUtc);
        Task ProcessWebhookAsync(string rawBody, string signature, DateTime nowUtc);
        Task<PaymentStatusDto> GetBookingPaymentStatusAsync(string currentUserId, string userRole, string bookingId);
        Task<List<RefundQueueItemDto>> GetRefundQueueAsync(int limit = 50);
        Task CompleteRefundAsync(string adminUserId, string refundId, CompleteRefundDto dto, DateTime nowUtc);
        Task UpdateRefundAccountAsync(string ownerUserId, string bookingId, UpdateRefundAccountDto dto);
    }
}
