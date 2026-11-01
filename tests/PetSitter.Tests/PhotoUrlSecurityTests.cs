using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace PetSitter.Tests;

public class PhotoUrlSecurityTests
{
    private static bool IsValidPhotoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var trimmed = url.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.startsWithVbscript() || lower.StartsWith("//"))
            return false;

        if (lower.StartsWith("https://")) return true;
        if (trimmed.StartsWith("/uploads/")) return true;

        return false;
    }

    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JAVASCRIPT:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("  javascript:void(0)", false)]
    [InlineData("//evil.com/fake.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("/assets/placeholder.png", false)]
    [InlineData("http://insecure.com/photo.jpg", false)]
    [InlineData("https://secure.com/photo.jpg", true)]
    [InlineData("/uploads/pet.jpg", true)]
    [InlineData("/uploads/avatars/user1.png", true)]
    public void TestPhotoUrlValidation(string? url, bool expected)
    {
        Assert.Equal(expected, IsValidPhotoUrl(url));
    }

    [Fact]
    public void TestStayUpdatesJsMatchesRule()
    {
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "PetSitter", "wwwroot", "js", "stay-updates.js");
        if (!File.Exists(jsPath))
        {
            // fallback if running from different test path
            jsPath = Path.GetFullPath(@"c:\PetSitter\PetSitter\wwwroot\js\stay-updates.js");
        }

        Assert.True(File.Exists(jsPath), $"File stay-updates.js must exist at {jsPath}");
        var content = File.ReadAllText(jsPath);

        // Đảm bảo stay-updates.js chỉ cho phép https:// và /uploads/
        Assert.Contains("lower.startsWith('https://')", content);
        Assert.Contains("trimmed.startsWith('/uploads/')", content);
        Assert.DoesNotContain("lower.startsWith('http://')", content);
    }
}

internal static class StringExtensions
{
    public static bool startsWithVbscript(this string s) => s.StartsWith("vbscript:");
}

