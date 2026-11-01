using System;
using System.Collections.Generic;
using System.IO;

namespace PetSitter.Services
{
    public class BankStatementRow
    {
        public int LineNumber { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string ReferenceCode { get; set; } = string.Empty;
        public long? ExtractedOrderCode { get; set; }
        public string RawLine { get; set; } = string.Empty;
        public string? ParseError { get; set; }
    }

    public class BankStatementParseResult
    {
        public List<BankStatementRow> Rows { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int TotalLines { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
    }

    public interface IBankStatementParser
    {
        BankStatementParseResult Parse(Stream stream);
        BankStatementParseResult Parse(string csvContent);
    }
}

