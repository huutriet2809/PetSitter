using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class CsvBankStatementParserTests
    {
        [Fact]
        public void Parse_WithUtf8Bom_RemovesBomAndParsesSuccessfully()
        {
            // Chuỗi CSV có BOM \uFEFF ở đầu
            var csvWithBom = "\uFEFFNgay,MoTa,SoTien,MaThamChieu\r\n01/11/2026,Chuyen tien don hang 123456,1.250.000,REF_VCB\r\n";
            var parser = new BankStatementParser();

            var result = parser.Parse(csvWithBom);

            Assert.Single(result.Rows);
            var row = result.Rows[0];
            Assert.Equal(1250000, row.Amount);
            Assert.Equal("REF_VCB", row.ReferenceCode);
            Assert.Equal(123456, row.ExtractedOrderCode);
            Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), row.TransactionDate);
        }

        [Theory]
        [InlineData("1.250.000", 1250000)]
        [InlineData("1.250.000,00", 1250000)]
        [InlineData("1,250,000", 1250000)]
        [InlineData("1,250,000.50", 1250000)]
        [InlineData("1250000", 1250000)]
        [InlineData("1.250.000 ₫", 1250000)]
        [InlineData("1.250.000 VND", 1250000)]
        [InlineData("+1.250.000", 1250000)]
        [InlineData("+1.250.000 ₫", 1250000)]
        [InlineData("-500.000", -500000)]
        [InlineData("-500.000 ₫", -500000)]
        public void ParseVietnameseAmount_VariousFormats_ReturnsCorrectLong(string rawAmount, long expected)
        {
            var parsed = BankStatementParser.ParseVietnameseAmount(rawAmount);
            Assert.Equal(expected, parsed);
        }

        [Fact]
        public void Parse_SignedAmountsInCsv_ParsesAmountDirectlyAndOnlySanitizesTextFields()
        {
            var csv = @"Ngay,MoTa,SoTien,MaThamChieu
01/11/2026,=SUM(1+1),+1.250.000 ₫,REF01
02/11/2026,-dangerous_formula,-500.000,REF02
";
            var parser = new BankStatementParser();
            var result = parser.Parse(csv);

            Assert.Equal(2, result.Rows.Count);

            // Số tiền: parse trực tiếp thành số học nguyên, không bị biến đổi bởi sanitize
            Assert.Equal(1250000, result.Rows[0].Amount);
            Assert.Equal(-500000, result.Rows[1].Amount);

            // Text: ô văn bản bắt đầu bằng = hoặc - được sanitize bằng dấu '
            Assert.Equal("'=SUM(1+1)", result.Rows[0].Description);
            Assert.Equal("'-dangerous_formula", result.Rows[1].Description);
        }

        [Fact]
        public void Parse_EmptyAndMalformedLines_HandledGracefullyWithoutCrashing()
        {
            var csv = @"Ngay,MoTa,SoTien,MaThamChieu

01/11/2026,Thanh toan hop le,500.000,REF1001

, , , 
02/11/2026,Thieu so tien
03/11/2026,So tien sai dinh dang,ABC_XYZ,REF1002
";
            var parser = new BankStatementParser();
            var result = parser.Parse(csv);

            // Dòng hợp lệ: dòng 3 (REF1001)
            // Dòng lỗi: dòng 6 (thiếu cột), dòng 7 (số tiền không hợp lệ)
            // Dòng trống: dòng 2, dòng 4, dòng 5 (toàn khoảng trắng) -> được bỏ qua
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(2, result.ErrorCount);
            Assert.NotEmpty(result.Warnings);
        }

        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("@SUM(A1:A10)", "'@SUM(A1:A10)")]
        [InlineData("-cmd|' /C calc'!A0", "'-cmd|' /C calc'!A0")]
        [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
        [InlineData("NoiDungBinhThuong", "NoiDungBinhThuong")]
        public void SanitizeField_FormulaInjection_PrefixesSingleQuote(string input, string expected)
        {
            var sanitized = BankStatementParser.SanitizeField(input);
            Assert.Equal(expected, sanitized);
        }

        [Fact]
        public void Parse_WithCustomConfiguredColumnMapping_MapsCorrectly()
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["BankStatementMapping:DateColumn"] = "CustomDate",
                ["BankStatementMapping:DescriptionColumn"] = "CustomDetails",
                ["BankStatementMapping:AmountColumn"] = "CustomValue",
                ["BankStatementMapping:ReferenceColumn"] = "CustomRef"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemoryConfig)
                .Build();

            var csv = "CustomDate,CustomDetails,CustomValue,CustomRef\r\n2026-11-01,Chuyen khoan don #888999,2.000.000,REF_CUSTOM\r\n";
            var parser = new BankStatementParser(configuration);

            var result = parser.Parse(csv);

            Assert.Single(result.Rows);
            var row = result.Rows[0];
            Assert.Equal(2000000, row.Amount);
            Assert.Equal("REF_CUSTOM", row.ReferenceCode);
            Assert.Equal(888999, row.ExtractedOrderCode);
        }

        [Fact]
        public void ExtractOrderCode_FromReferenceOrDescription_ExtractsCorrectly()
        {
            // Trích từ ReferenceCode
            var code1 = BankStatementParser.ExtractOrderCode("1730456789", "Chuyen tien");
            Assert.Equal(1730456789, code1);

            // Trích từ Description với prefix ORDER
            var code2 = BankStatementParser.ExtractOrderCode("VCB001", "Thanh toan ORDER1730456789 tai PawStay");
            Assert.Equal(1730456789, code2);

            // Trích từ Description với prefix DH
            var code3 = BankStatementParser.ExtractOrderCode("", "Thanh toan DH99887766");
            Assert.Equal(99887766, code3);
        }
    }
}

