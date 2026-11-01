using PetSitter.Models;
using System;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class EmailMessage
    {
        public string ToEmail { get; set; } = string.Empty;
        public string ToName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string DedupeKey { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public interface IEmailService
    {
        Task QueueEmailAsync(string toEmail, string toName, string subject, string bodyHtml, string dedupeKey, string type);

        Task SendBookingCreatedAsync(string ownerEmail, string ownerName, string sitterEmail, string sitterName, Booking booking);

        Task SendPaymentSuccessAsync(string ownerEmail, string ownerName, Booking booking, long amount);

        Task SendBookingAcceptedAsync(string ownerEmail, string ownerName, Booking booking, DateTime paymentDeadline);

        Task SendBookingRejectedAsync(string ownerEmail, string ownerName, Booking booking, string reason);

        Task SendBookingCompletedAsync(string ownerEmail, string ownerName, string sitterEmail, string sitterName, Booking booking, bool isAuto = false);

        Task SendRefundNoticeAsync(string ownerEmail, string ownerName, Booking booking, long refundAmount, string reason);

        Task SendOverdueReminderAsync(string sitterEmail, string sitterName, Booking booking, int daysOverdue);

        Task SendAccountStatusNoticeAsync(string userEmail, string userName, bool isActive);
    }
}

