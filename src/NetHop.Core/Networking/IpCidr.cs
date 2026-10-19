using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace NetHop.Core.Networking;

/// <summary>
/// A host address together with its prefix length (<c>192.168.1.10/24</c>).
/// Unlike <see cref="IPNetwork"/>, host bits are allowed: this is an interface address, not a subnet.
/// </summary>
public readonly record struct IpCidr(IPAddress Address, int PrefixLength)
{
    public AddressFamily Family => Address.AddressFamily;

    public bool IsIPv4 => Family == AddressFamily.InterNetwork;

    public bool IsIPv6 => Family == AddressFamily.InterNetworkV6;

    public static int MaxPrefix(AddressFamily family) => family == AddressFamily.InterNetwork ? 32 : 128;

    public static bool TryParse([NotNullWhen(true)] string? text, out IpCidr result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1)
            return false;

        if (!IPAddress.TryParse(text.AsSpan(0, slash).Trim(), out var address))
            return false;
        if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            return false;
        if (!int.TryParse(text.AsSpan(slash + 1).Trim(), out var prefix))
            return false;
        if (prefix < 1 || prefix > MaxPrefix(address.AddressFamily))
            return false;

        result = new IpCidr(address, prefix);
        return true;
    }

    public static IpCidr Parse(string text) =>
        TryParse(text, out var result) ? result : throw new FormatException($"'{text}' is not a valid address in CIDR notation.");

    /// <summary>True when <paramref name="other"/> falls in the same subnet as this address.</summary>
    public bool Contains(IPAddress other)
    {
        if (other.AddressFamily != Family)
            return false;

        var a = Address.GetAddressBytes();
        var b = other.GetAddressBytes();
        var fullBytes = PrefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        var remainingBits = PrefixLength % 8;
        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (a[fullBytes] & mask) == (b[fullBytes] & mask);
    }

    /// <summary>For IPv4 subnets larger than /31: the address is the network or broadcast address.</summary>
    public bool IsNetworkOrBroadcastAddress()
    {
        if (!IsIPv4 || PrefixLength >= 31)
            return false;

        var value = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(Address.GetAddressBytes()));
        var hostMask = uint.MaxValue >> PrefixLength;
        var host = value & hostMask;
        return host == 0 || host == hostMask;
    }

    public override string ToString() => $"{Address}/{PrefixLength}";
}
