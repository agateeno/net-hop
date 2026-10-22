namespace NetHop.Core.Profiles;

/// <summary>
/// Domains a profile may not put in the hosts file (protected-domains.txt, embedded): redirecting them would cut the PC
/// off from updates, sign-in or certificate checks, and Microsoft Defender reports it as a threat.
/// </summary>
public static class ProtectedDomains
{
    private const string ResourceName = "NetHop.Core.protected-domains.txt";

    private static readonly Lazy<IReadOnlyList<string>> Domains = new(Load);

    public static IReadOnlyList<string> All => Domains.Value;

    /// <summary>The protected domain that <paramref name="name"/> is, or is a subdomain of; null when there is none.</summary>
    public static string? Find(string name)
    {
        var host = name.Trim().TrimEnd('.').ToLowerInvariant();
        return Domains.Value.FirstOrDefault(domain => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal));
    }

    internal static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n')
            .Select(line => line.Split('#')[0].Trim().ToLowerInvariant())
            .Where(line => line.Length > 0)
            .ToList();

    private static IReadOnlyList<string> Load()
    {
        using var stream = typeof(ProtectedDomains).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is missing from NetHop.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
