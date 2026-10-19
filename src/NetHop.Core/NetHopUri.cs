using System.Diagnostics.CodeAnalysis;

namespace NetHop.Core;

public enum UriCommand
{
    Apply,
    Next,
    Open,
}

/// <summary>
/// A parsed activation URI: <c>nethop://apply/Office</c>, <c>nethop://next</c> or <c>nethop://open</c>.
/// Used by Power Automate, Logi Options+, shortcuts and links.
/// </summary>
public sealed record NetHopUri(UriCommand Command, string? Profile = null)
{
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out NetHopUri? result)
    {
        result = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(NetHopConstants.UriScheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var argument = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        switch (uri.Host.ToLowerInvariant())
        {
            case "apply" when argument.Length > 0:
                result = new NetHopUri(UriCommand.Apply, argument);
                return true;
            case "next":
                result = new NetHopUri(UriCommand.Next);
                return true;
            case "open" or "":
                result = new NetHopUri(UriCommand.Open);
                return true;
            default:
                return false;
        }
    }

    public override string ToString() => Command switch
    {
        UriCommand.Apply => $"{NetHopConstants.UriScheme}://apply/{Uri.EscapeDataString(Profile ?? "")}",
        UriCommand.Next => $"{NetHopConstants.UriScheme}://next",
        _ => $"{NetHopConstants.UriScheme}://open",
    };
}
