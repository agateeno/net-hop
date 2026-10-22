using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetHop.Core.Profiles;

namespace NetHop.Core.Networking;

/// <summary>One host name in the hosts file. Names are kept lowercase: lookups ignore case.</summary>
public sealed record HostsMapping(IPAddress Address, string Name);

/// <summary>
/// NetHop's block of the Windows hosts file. NetHop reads and rewrites only the lines between its markers, one name
/// per line (Windows reads only the first few names of a line); everything else in the file is kept byte for byte.
/// </summary>
public static partial class HostsFile
{
    public const string BeginMarker = "# BEGIN NetHop (managed by NetHop, do not edit this block)";
    public const string EndMarker = "# END NetHop";

    private const string BeginPrefix = "# BEGIN NetHop";

    /// <summary>The mappings in NetHop's block; empty when the file has none.</summary>
    /// <exception cref="FormatException">The block has a start but no end.</exception>
    public static IReadOnlyList<HostsMapping> ReadBlock(string content)
    {
        var lines = SplitLines(content);
        var (begin, end) = FindBlock(lines);
        var result = new List<HostsMapping>();
        if (begin < 0)
            return result;
        for (var i = begin + 1; i < end; i++)
        {
            var line = lines[i];
            var comment = line.IndexOf('#');
            var parts = (comment >= 0 ? line[..comment] : line).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out var address))
                continue;
            result.AddRange(parts.Skip(1).Select(name => new HostsMapping(address, name.ToLowerInvariant())));
        }
        return result;
    }

    /// <summary>
    /// The file content with NetHop's block replaced by <paramref name="mappings"/>: in place when there is one,
    /// appended otherwise. No mappings removes the block. Line endings follow the file's own.
    /// </summary>
    /// <exception cref="FormatException">The block has a start but no end: the file is left for the user to fix.</exception>
    public static string WriteBlock(string content, IReadOnlyList<HostsMapping> mappings)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) || !content.Contains('\n') ? "\r\n" : "\n";
        var lines = SplitLines(content);
        var (begin, end) = FindBlock(lines);

        List<string> block = mappings.Count == 0
            ? []
            : [BeginMarker, .. mappings.Select(m => $"{m.Address} {m.Name}"), EndMarker];

        if (begin >= 0)
        {
            lines.RemoveRange(begin, end - begin + 1);
            lines.InsertRange(begin, block);
        }
        else if (block.Count > 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add("");
            lines.AddRange(block);
        }

        return lines.Count == 0 ? "" : string.Join(newline, lines) + newline;
    }

    /// <summary>A profile's entries as the lines they become: one mapping per name, in the profile's order, without repeats.</summary>
    public static IReadOnlyList<HostsMapping> ToMappings(IEnumerable<HostsEntry> entries)
    {
        var result = new List<HostsMapping>();
        foreach (var entry in entries)
        {
            if (!TryParseAddress(entry.Ip, out var address))
                continue;
            foreach (var name in entry.Names)
            {
                var mapping = new HostsMapping(address, name.Trim().ToLowerInvariant());
                if (!result.Contains(mapping))
                    result.Add(mapping);
            }
        }
        return result;
    }

    /// <summary>
    /// A plain IPv4 (dotted quad) or IPv6 address. Stricter than <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>,
    /// which also takes forms like "10.1" or a scope id that the hosts file does not.
    /// </summary>
    public static bool TryParseAddress(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Contains('%'))
            return false;
        if (!text.Contains(':') && !Ipv4Regex().IsMatch(text))
            return false;
        return IPAddress.TryParse(text, out address)
            && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
    }

    /// <summary>Letters, digits and hyphens in dot-separated labels (RFC 1123): nothing that could end the line or start a comment.</summary>
    public static bool IsValidHostName(string? name) =>
        name is { Length: > 0 and <= 253 } && HostNameRegex().IsMatch(name);

    private static List<string> SplitLines(string content)
    {
        if (content.Length == 0)
            return [];
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (content.EndsWith('\n'))
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static (int Begin, int End) FindBlock(List<string> lines)
    {
        var begin = lines.FindIndex(l => l.TrimStart().StartsWith(BeginPrefix, StringComparison.OrdinalIgnoreCase));
        if (begin < 0)
            return (-1, -1);
        var end = lines.FindIndex(begin + 1, l => l.Trim().Equals(EndMarker, StringComparison.OrdinalIgnoreCase));
        if (end < 0)
            throw new FormatException("The hosts file has the start of NetHop's block but not its end: fix it by hand.");
        return (begin, end);
    }

    // \z, not $: $ also matches before a final line break.
    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}\z")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\z")]
    private static partial Regex HostNameRegex();
}
