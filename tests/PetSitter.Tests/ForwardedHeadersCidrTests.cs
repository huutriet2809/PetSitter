using System.Collections.Generic;
using System.Net;
using PetSitter.Helpers;
using Xunit;

namespace PetSitter.Tests
{
    public class ForwardedHeadersCidrTests
    {
        [Theory]
        [InlineData("10.0.0.0/8", "10.0.0.0", 8)]
        [InlineData("172.16.0.0/12", "172.16.0.0", 12)]
        [InlineData("192.168.1.0/24", "192.168.1.0", 24)]
        [InlineData("127.0.0.1/32", "127.0.0.1", 32)]
        [InlineData("::1/128", "::1", 128)]
        [InlineData("2001:db8::/32", "2001:db8::", 32)]
        public void TryParseCidr_ValidCidr_ReturnsTrueAndCorrectPrefixAndLength(string cidr, string expectedIp, int expectedPrefixLength)
        {
            var success = CidrNetworkHelper.TryParseCidr(cidr, out var prefix, out var prefixLength);

            Assert.True(success);
            Assert.Equal(IPAddress.Parse(expectedIp), prefix);
            Assert.Equal(expectedPrefixLength, prefixLength);
        }

        [Theory]
        [InlineData("invalid_cidr")]
        [InlineData("192.168.1.1/33")] // IPv4 prefix không được > 32
        [InlineData("256.0.0.1/24")]   // IP không hợp lệ
        [InlineData("10.0.0.1/-1")]    // Prefix âm
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void TryParseCidr_InvalidCidr_ReturnsFalse(string? cidr)
        {
            var success = CidrNetworkHelper.TryParseCidr(cidr, out _, out _);
            Assert.False(success);
        }

        [Fact]
        public void ParseCidrNetworks_MixedList_FiltersOutInvalidEntriesWithoutThrowing()
        {
            var mixedList = new List<string>
            {
                "10.0.0.0/8",
                "invalid_network",
                "192.168.0.0/16",
                "",
                "256.256.256.256/24"
            };

            var parsed = CidrNetworkHelper.ParseCidrNetworks(mixedList);

            Assert.Equal(2, parsed.Count);
            Assert.Contains(parsed, n => n.Prefix.Equals(IPAddress.Parse("10.0.0.0")) && n.PrefixLength == 8);
            Assert.Contains(parsed, n => n.Prefix.Equals(IPAddress.Parse("192.168.0.0")) && n.PrefixLength == 16);
        }
    }
}

