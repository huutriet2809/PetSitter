using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace PetSitter.Services
{
    public class BankStatementParser : IBankStatementParser
    {
        private readonly IConfiguration? _configuration;

        public BankStatementParser(IConfiguration? configuration = null)
        {
            _configuration = configuration;
        }

        public BankStatementParseResult Parse(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var content = reader.ReadToEnd();
            return Parse(content);
        }

        public BankStatementParseResult Parse(string csvContent)
        {
            var result = new BankStatementParseResult();
            if (string.IsNullOrWhiteSpace(csvContent))
            {
                result.Warnings.Add("Tệp CSV rỗng hoặc chỉ chứa khoảng trắng.");
                return result;
            }

            // Loại bỏ BOM nếu còn sót lại ở đầu chuỗi
            if (csvContent.StartsWith("\uFEFF", StringComparison.Ordinal))
            {
                csvContent = csvContent.Substring(1);
            }

            var rawLines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            result.TotalLines = rawLines.Length;

            if (rawLines.Length == 0)
            {
                result.Warnings.Add("Không tìm thấy dòng nào trong tệp CSV.");
                return result;
            }

            // Tìm dòng header đầu tiên không rỗng
            int headerIndex = -1;
            List<string> headers = new();
            for (int i = 0; i < rawLines.Length; i++)
            {
                var trimmed = rawLines[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    headerIndex = i;
                    headers = SplitCsvLine(trimmed).Select(h => SanitizeField(h).Trim()).ToList();
                    break;
                }
            }

            if (headerIndex == -1 || headers.Count == 0)
            {
                result.Warnings.Add("Không thể xác định hàng tiêu đề (header) của tệp CSV.");
                return result;
            }

            // Cấu hình mapping cột từ configuration
            var configuredDateCol = _configuration?["BankStatementMapping:DateColumn"]?.Trim();
            var configuredDescCol = _configuration?["BankStatementMapping:DescriptionColumn"]?.Trim();
            var configuredAmountCol = _configuration?["BankStatementMapping:AmountColumn"]?.Trim();
            var configuredRefCol = _configuration?["BankStatementMapping:ReferenceColumn"]?.Trim();

            int dateIdx = FindColumnIndex(headers, configuredDateCol, new[] { "ngay", "date", "thoigian", "time", "ngaygiaodich", "transactiondate" });
            int descIdx = FindColumnIndex(headers, configuredDescCol, new[] { "mota", "description", "noidung", "chitiet", "noidungchuyenkhoan", "memo", "remarks" });
            int amountIdx = FindColumnIndex(headers, configuredAmountCol, new[] { "sotien", "amount", "giatri", "credit", "phatsinhtang", "tienvao" });
            int refIdx = FindColumnIndex(headers, configuredRefCol, new[] { "mathamchieu", "reference", "refno", "referencecode", "magiaodich", "transid", "txid", "ordercode" });

            if (amountIdx == -1)
            {
                result.Warnings.Add("Không tìm thấy cột Số tiền (Amount / SoTien) trong tệp CSV.");
                return result;
            }

            for (int i = headerIndex + 1; i < rawLines.Length; i++)
            {
                var rawLine = rawLines[i].Trim();
                if (string.IsNullOrEmpty(rawLine)) continue;

                var columns = SplitCsvLine(rawLine);
                if (columns.All(string.IsNullOrWhiteSpace)) continue;

                var row = new BankStatementRow
                {
                    LineNumber = i + 1,
                    RawLine = rawLine
                };

                try
                {
                    // 1. Parse Số tiền (Amount)
                    if (amountIdx < columns.Count)
                    {
                        var rawAmount = columns[amountIdx];
                        row.Amount = ParseVietnameseAmount(rawAmount);
                    }
                    else
                    {
                        throw new FormatException("Dòng không đủ số cột để đọc số tiền.");
                    }

                    // 2. Parse Ngày giao dịch (Date)
                    if (dateIdx != -1 && dateIdx < columns.Count)
                    {
                        row.TransactionDate = ParseDate(columns[dateIdx]);
                    }

                    // 3. Parse Mô tả (Description) & Sanitize chống CSV Injection
                    if (descIdx != -1 && descIdx < columns.Count)
                    {
                        row.Description = SanitizeField(columns[descIdx]);
                    }

                    // 4. Parse Mã tham chiếu (Reference) & Sanitize
                    if (refIdx != -1 && refIdx < columns.Count)
                    {
                        row.ReferenceCode = SanitizeField(columns[refIdx]);
                    }

                    // 5. Trích xuất OrderCode (từ ReferenceCode hoặc Description)
                    row.ExtractedOrderCode = ExtractOrderCode(row.ReferenceCode, row.Description);

                    result.Rows.Add(row);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    row.ParseError = ex.Message;
                    result.Rows.Add(row);
                    result.ErrorCount++;
                    result.Warnings.Add($"Dòng {i + 1}: Lỗi phân tích cú pháp ({ex.Message})");
                }
            }

            return result;
        }

        public static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            var inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    // Escape quote inside quote: "" -> "
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip next quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString().Trim());
            return result;
        }

        public static string SanitizeField(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            var trimmed = input.Trim();
            if (trimmed.Length == 0) return string.Empty;

            // Chống CSV Injection: nếu cell bắt đầu bằng '=', '+', '-', '@', '\t', '\r'
            // Đối với các ký tự công thức, loại bỏ ký tự dẫn đầu nếu là text không phải số
            char first = trimmed[0];
            if (first == '=' || first == '@')
            {
                return "'" + trimmed;
            }

            if ((first == '+' || first == '-') && !decimal.TryParse(trimmed.Replace(".", "").Replace(",", "."), out _))
            {
                return "'" + trimmed;
            }

            return trimmed;
        }

        public static long ParseVietnameseAmount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;

            var clean = raw.Trim()
                .Replace("₫", "")
                .Replace("VND", "", StringComparison.OrdinalIgnoreCase)
                .Replace("vnd", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "")
                .Replace("\u00A0", ""); // Non-breaking space

            // Nếu bắt đầu bằng dấu + hoặc -
            var isNegative = clean.StartsWith("-");
            clean = clean.TrimStart('+', '-');

            if (string.IsNullOrWhiteSpace(clean)) return 0;

            // Trường hợp có cả dấu . và dấu ,: ví dụ 1.250.000,00 hoặc 1,250,000.00
            if (clean.Contains('.') && clean.Contains(','))
            {
                int lastDot = clean.LastIndexOf('.');
                int lastComma = clean.LastIndexOf(',');
                if (lastDot > lastComma)
                {
                    // US: 1,250,000.50 -> bỏ dấu phẩy, lấy phần nguyên trước dấu chấm
                    clean = clean.Substring(0, lastDot).Replace(",", "");
                }
                else
                {
                    // VN: 1.250.000,50 -> bỏ dấu chấm, lấy phần nguyên trước dấu phẩy
                    clean = clean.Substring(0, lastComma).Replace(".", "");
                }
            }
            else if (clean.Contains('.'))
            {
                // Chỉ có dấu chấm: có thể là 1.250.000 (thousands) hoặc 1250.50 (decimal)
                var parts = clean.Split('.');
                if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3))
                {
                    // 1.250.000 hoặc 1.250 -> thousands separator kiểu VN
                    clean = clean.Replace(".", "");
                }
                else
                {
                    // Decimal separator
                    clean = parts[0];
                }
            }
            else if (clean.Contains(','))
            {
                // Chỉ có dấu phẩy: có thể là 1,250,000 (thousands) hoặc 1250,50 (decimal)
                var parts = clean.Split(',');
                if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3))
                {
                    clean = clean.Replace(",", "");
                }
                else
                {
                    clean = parts[0];
                }
            }

            if (long.TryParse(clean, out var val))
            {
                return isNegative ? -val : val;
            }

            throw new FormatException($"Không thể chuyển đổi chuỗi '{raw}' thành số tiền hợp lệ.");
        }

        public static DateTime? ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var clean = raw.Trim();
            string[] formats =
            {
                "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
                "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"
            };

            if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            if (DateTime.TryParse(clean, new CultureInfo("vi-VN"), DateTimeStyles.None, out var viDt))
            {
                return DateTime.SpecifyKind(viDt, DateTimeKind.Utc);
            }

            if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invDt))
            {
                return DateTime.SpecifyKind(invDt, DateTimeKind.Utc);
            }

            return null;
        }

        public static long? ExtractOrderCode(string referenceCode, string description)
        {
            // 1. Ưu tiên ReferenceCode nếu là số nguyên từ 6 đến 18 chữ số
            if (!string.IsNullOrWhiteSpace(referenceCode))
            {
                var cleanRef = Regex.Replace(referenceCode, @"[^\d]", "");
                if (cleanRef.Length >= 6 && cleanRef.Length <= 18 && long.TryParse(cleanRef, out var refCode))
                {
                    return refCode;
                }
            }

            // 2. Tìm trong Description các mẫu như ORDER123456, DH123456, PAW123456 hoặc chuỗi số độc lập
            if (!string.IsNullOrWhiteSpace(description))
            {
                var matchPrefix = Regex.Match(description, @"(?:ORDER|DH|PAW|BILL|BOOKING|MDL)?\s*#?(\d{6,18})", RegexOptions.IgnoreCase);
                if (matchPrefix.Success && long.TryParse(matchPrefix.Groups[1].Value, out var orderCode))
                {
                    return orderCode;
                }

                // Tìm bất kỳ chuỗi số nào từ 6 đến 18 chữ số
                var matchDigits = Regex.Match(description, @"\b(\d{6,18})\b");
                if (matchDigits.Success && long.TryParse(matchDigits.Groups[1].Value, out var plainDigits))
                {
                    return plainDigits;
                }
            }

            return null;
        }

        private static int FindColumnIndex(List<string> headers, string? configuredName, string[] candidateNames)
        {
            if (!string.IsNullOrWhiteSpace(configuredName))
            {
                var idx = headers.FindIndex(h => string.Equals(NormalizeColumnName(h), NormalizeColumnName(configuredName), StringComparison.OrdinalIgnoreCase));
                if (idx != -1) return idx;
            }

            foreach (var candidate in candidateNames)
            {
                var idx = headers.FindIndex(h => NormalizeColumnName(h).Contains(candidate, StringComparison.OrdinalIgnoreCase));
                if (idx != -1) return idx;
            }

            return -1;
        }

        private static string NormalizeColumnName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c) && c != '_' && c != '-')
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }
    }
}

