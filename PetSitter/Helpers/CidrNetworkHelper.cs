using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;

namespace PetSitter.Helpers
{
    public static class CidrNetworkHelper
    {
        public static bool TryParseCidr(string? cidr, out IPAddress prefix, out int prefixLength)
        {
            prefix = IPAddress.None;
            prefixLength = 0;

            if (string.IsNullOrWhiteSpace(cidr)) return false;

            var trimmed = cidr.Trim();

            // Sử dụng System.Net.IPNetwork của .NET 8 nếu có
            if (System.Net.IPNetwork.TryParse(trimmed, out var net))
            {
                prefix = net.BaseAddress;
                prefixLength = net.PrefixLength;
                return true;
            }

            // Fallback: phân tích cú pháp IP/PrefixLength thủ công
            var parts = trimmed.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0].Trim(), out var parsedIp) &&
                int.TryParse(parts[1].Trim(), out var parsedPrefix))
            {
                int maxBits = parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
                if (parsedPrefix >= 0 && parsedPrefix <= maxBits)
                {
                    prefix = parsedIp;
                    prefixLength = parsedPrefix;
                    return true;
                }
            }

            return false;
        }

        public static List<Microsoft.AspNetCore.HttpOverrides.IPNetwork> ParseCidrNetworks(IEnumerable<string>? cidrList, ILogger? logger = null)
        {
            var networks = new List<Microsoft.AspNetCore.HttpOverrides.IPNetwork>();
            if (cidrList == null) return networks;

            foreach (var cidr in cidrList)
            {
                if (string.IsNullOrWhiteSpace(cidr)) continue;

                if (TryParseCidr(cidr, out var prefix, out var prefixLength))
                {
                    networks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
                }
                else
                {
                    logger?.LogWarning("CẢNH BÁO BẢO MẬT: Dải mạng CIDR '{Cidr}' không hợp lệ và đã bị bỏ qua.", cidr);
                }
            }

            return networks;
        }
    }
}

