using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PetSitter.Models;

namespace PetSitter.Services
{
    public interface IBankReconciliationService
    {
        Task<BankReconciliationRun> ReconcileStatementAsync(Stream fileStream, string fileName, long fileSizeBytes, string adminUserId);
        Task<List<BankReconciliationRun>> GetReconciliationHistoryAsync(int limit = 20);
        Task<BankReconciliationRun?> GetReconciliationDetailsAsync(string runId);
    }
}

