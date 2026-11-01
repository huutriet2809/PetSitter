using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using MongoDB.Driver;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PetSitter.BackgroundServices
{
    /// <summary>
    /// Worker ngầm tiêu thụ hàng đợi Channel&lt;EmailMessage&gt;, gửi email qua SMTP MailKit,
    /// có retry tối đa 3 lần, ghi log và chống gửi trùng bằng DedupeKey.
    /// </summary>
    public class EmailBackgroundService : BackgroundService
    {
        private readonly IEmailQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailBackgroundService> _logger;

        public EmailBackgroundService(
            IEmailQueue queue,
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<EmailBackgroundService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[EmailWorker] EmailBackgroundService đã khởi động.");

            await foreach (var message in _queue.ReadAllAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await ProcessMessageWithRetryAsync(message, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EmailWorker] Lỗi không xử lý được khi gửi mail đến {ToEmail} (Subject: {Subject}).", message.ToEmail, message.Subject);
                }
            }

            _logger.LogInformation("[EmailWorker] EmailBackgroundService đã dừng.");
        }

        private async Task ProcessMessageWithRetryAsync(EmailMessage message, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PetSitterContext>();

            // 1. Kiểm tra DedupeKey trong MongoDB
            if (!string.IsNullOrWhiteSpace(message.DedupeKey))
            {
                var alreadySent = await context.EmailLogs
                    .Find(e => e.DedupeKey == message.DedupeKey && e.Status == "SENT")
                    .AnyAsync(ct);

                if (alreadySent)
                {
                    _logger.LogDebug("[EmailWorker] Đã gửi mail trước đó cho DedupeKey {DedupeKey}. Bỏ qua.", message.DedupeKey);
                    return;
                }
            }

            var enabled = _config.GetValue<bool>("SmtpSettings:Enabled", false);
            var host = _config["SmtpSettings:Host"] ?? "localhost";
            var port = _config.GetValue<int>("SmtpSettings:Port", 1025);
            var user = _config["SmtpSettings:User"] ?? string.Empty;
            var pass = _config["SmtpSettings:Password"] ?? string.Empty;
            var fromEmail = _config["SmtpSettings:FromEmail"] ?? "no-reply@pawstay.vn";
            var fromName = _config["SmtpSettings:FromName"] ?? "PawStay Pet Care";
            var useSsl = _config.GetValue<bool>("SmtpSettings:UseSsl", false);

            var logEntry = new EmailLog
            {
                DedupeKey = message.DedupeKey,
                ToEmail = message.ToEmail,
                ToName = message.ToName,
                Subject = message.Subject,
                BodyHtml = message.BodyHtml,
                Type = message.Type,
                Status = "PENDING",
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            await context.EmailLogs.InsertOneAsync(logEntry, cancellationToken: ct);

            // Nếu SMTP không bật (môi trường test/dev mà không có SMTP server): ghi nhận mô phỏng thành công
            if (!enabled)
            {
                _logger.LogInformation("[EmailWorker] [MOCK SEND] Gửi mail (SMTP disabled). Đến: {ToEmail} | Tiêu đề: {Subject}", message.ToEmail, message.Subject);

                await context.EmailLogs.UpdateOneAsync(
                    e => e.Id == logEntry.Id,
                    Builders<EmailLog>.Update
                        .Set(e => e.Status, "SENT")
                        .Set(e => e.SentAt, DateTime.UtcNow)
                        .Set(e => e.Attempts, 1),
                    cancellationToken: ct
                );
                return;
            }

            // Chuẩn bị MimeMessage
            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(fromName, fromEmail));
            mimeMessage.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
            mimeMessage.Subject = message.Subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = message.BodyHtml };
            mimeMessage.Body = bodyBuilder.ToMessageBody();

            int maxRetries = 3;
            bool sent = false;
            string? lastError = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    using var client = new SmtpClient();
                    // Bỏ qua kiểm tra chứng chỉ cho môi trường phát triển nếu cần
                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                    var secureOption = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
                    if (port == 25 || port == 1025)
                    {
                        secureOption = SecureSocketOptions.None;
                    }

                    await client.ConnectAsync(host, port, secureOption, ct);

                    if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
                    {
                        await client.AuthenticateAsync(user, pass, ct);
                    }

                    await client.SendAsync(mimeMessage, ct);
                    await client.DisconnectAsync(true, ct);

                    sent = true;
                    _logger.LogInformation("[EmailWorker] Đã gửi email thành công đến {ToEmail} (Lần thử {Attempt}).", message.ToEmail, attempt);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning(ex, "[EmailWorker] Lần thử {Attempt}/{Max} thất bại khi gửi mail tới {ToEmail}: {Message}", attempt, maxRetries, message.ToEmail, ex.Message);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                    }
                }
            }

            var updateBuilder = Builders<EmailLog>.Update
                .Set(e => e.Attempts, maxRetries)
                .Set(e => e.Status, sent ? "SENT" : "FAILED")
                .Set(e => e.ErrorMessage, lastError);

            if (sent)
            {
                updateBuilder = updateBuilder.Set(e => e.SentAt, DateTime.UtcNow);
            }

            await context.EmailLogs.UpdateOneAsync(e => e.Id == logEntry.Id, updateBuilder, cancellationToken: ct);
        }
    }
}

