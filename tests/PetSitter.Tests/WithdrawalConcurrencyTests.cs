using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class WithdrawalConcurrencyTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;
        private LedgerService _ledgerService = null!;
        private WithdrawalService _withdrawalService = null!;

        public async Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_Withdrawal_" + Guid.NewGuid().ToString("N")));

            // Unique index cho JournalEntry IdempotencyKey
            var keyBuilder = Builders<JournalEntry>.IndexKeys;
            var indexModel = new CreateIndexModel<JournalEntry>(
                keyBuilder.Ascending(j => j.IdempotencyKey),
                new CreateIndexOptions { Unique = true, Sparse = true });
            await _context.JournalEntries.Indexes.CreateOneAsync(indexModel);

            var configDict = new Dictionary<string, string?>
            {
                ["Payments:EarningsHoldHours"] = "48",
                ["Payments:CommissionBps"] = "1000"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

            _ledgerService = new LedgerService(_context);

            var gatewayMock = new Mock<IPaymentGateway>();
            var notifMock = new Mock<INotificationService>();
            var dataProtectionProvider = new EphemeralDataProtectionProvider();

            _withdrawalService = new WithdrawalService(
                _context,
                _ledgerService,
                gatewayMock.Object,
                notifMock.Object,
                dataProtectionProvider,
                config,
                NullLogger<WithdrawalService>.Instance
            );
        }

        public Task DisposeAsync()
        {
            _server?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ApproveWithdrawal_ConcurrentAdmins_OnlyOneSucceeds()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var withdrawalId = ObjectId.GenerateNewId().ToString();

            var req = new WithdrawalRequest
            {
                WithdrawalId = withdrawalId,
                ReferenceId = "WTH" + nowUtc.ToString("yyMMddHHmmss") + "01",
                SitterUserId = ObjectId.GenerateNewId().ToString(),
                Amount = 150000,
                Status = WithdrawalStatus.REQUESTED,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                MaskedAccountNumber = "******1234",
                BankBin = "970422",
                IdempotencyKey = "IDEMP_" + withdrawalId,
                EncryptedBankSnapshot = "ENCRYPTED"
            };
            await _context.WithdrawalRequests.InsertOneAsync(req);

            // Act: 2 Admin cùng approve đồng thời
            Task call1 = _withdrawalService.ApproveWithdrawalAsync("ADMIN_ALICE", withdrawalId, nowUtc);
            Task call2 = _withdrawalService.ApproveWithdrawalAsync("ADMIN_BOB", withdrawalId, nowUtc);

            int successCount = 0;
            int conflictCount = 0;

            var tasks = new[] { call1, call2 }.Select(async t =>
            {
                try
                {
                    await t;
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch (ConflictException)
                {
                    System.Threading.Interlocked.Increment(ref conflictCount);
                }
            });

            await Task.WhenAll(tasks);

            // Assert:
            // 1. Đúng 1 admin thành công và 1 admin nhận ConflictException
            Assert.Equal(1, successCount);
            Assert.Equal(1, conflictCount);

            // 2. Trạng thái trong DB chuyển sang APPROVED
            var updated = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            Assert.NotNull(updated);
            Assert.Equal(WithdrawalStatus.APPROVED, updated.Status);
        }

        [Fact]
        public async Task ConfirmPaid_ConcurrentAdmins_RecordsOnlyOneJournalEntry()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var withdrawalId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var refId = "WTH" + nowUtc.ToString("yyMMddHHmmss") + "02";

            var req = new WithdrawalRequest
            {
                WithdrawalId = withdrawalId,
                ReferenceId = refId,
                SitterUserId = sitterUserId,
                Amount = 200000,
                Status = WithdrawalStatus.PROCESSING,
                ProcessingByAdminUserId = "ADMIN_CLAIMER",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                MaskedAccountNumber = "******5678",
                BankBin = "970422",
                IdempotencyKey = "IDEMP_" + withdrawalId,
                EncryptedBankSnapshot = "ENCRYPTED"
            };
            await _context.WithdrawalRequests.InsertOneAsync(req);

            var earningsAccount = new EarningsAccount
            {
                AccountId = ObjectId.GenerateNewId().ToString(),
                UserId = sitterUserId,
                ReservedAmount = 200000,
                UpdatedAt = nowUtc
            };
            await _context.EarningsAccounts.InsertOneAsync(earningsAccount);

            var dto1 = new ConfirmPaidDto { BankTransactionRef = "FT2026110199991" };
            var dto2 = new ConfirmPaidDto { BankTransactionRef = "FT2026110199992" };

            // Act: 2 Admin cùng confirm-paid đồng thời
            Task call1 = _withdrawalService.ConfirmPaidAsync("ADMIN_ALICE", withdrawalId, dto1, nowUtc);
            Task call2 = _withdrawalService.ConfirmPaidAsync("ADMIN_BOB", withdrawalId, dto2, nowUtc);

            int successCount = 0;
            int errorCount = 0;

            var tasks = new[] { call1, call2 }.Select(async t =>
            {
                try
                {
                    await t;
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch
                {
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(tasks);

            // Assert:
            // 1. Chỉ 1 admin thành công chuyển trạng thái
            Assert.Equal(1, successCount);
            Assert.Equal(1, errorCount);

            // 2. Trạng thái trong DB chuyển sang PAID
            var updated = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            Assert.NotNull(updated);
            Assert.Equal(WithdrawalStatus.PAID, updated.Status);

            // 3. QUAN TRỌNG NHẤT: Bút toán sổ cái WITHDRAWAL_PAID chỉ được ghi đúng 1 lần
            var expectedKey = LedgerIdempotencyKeys.WithdrawalPaid(refId);
            var journalEntries = await _context.JournalEntries
                .Find(j => j.IdempotencyKey == expectedKey)
                .ToListAsync();

            Assert.Single(journalEntries);
            Assert.Equal(JournalType.WITHDRAWAL_PAID, journalEntries[0].Type);

            // 4. Số dư ReservedAmount trên ví Sitter chỉ bị trừ đúng 1 lần về 0
            var updatedAccount = await _context.EarningsAccounts.Find(a => a.UserId == sitterUserId).FirstOrDefaultAsync();
            Assert.NotNull(updatedAccount);
            Assert.Equal(0, updatedAccount.ReservedAmount);
        }
    }
}
