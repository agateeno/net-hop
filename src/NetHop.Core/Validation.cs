namespace NetHop.Core;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>A problem found while validating or planning. <see cref="Path"/> points into the YAML, e.g. <c>adapters[0].ipv4.addresses[1]</c>.</summary>
public sealed record Issue(IssueSeverity Severity, string Path, string Message)
{
    public static Issue Error(string path, string message) => new(IssueSeverity.Error, path, message);

    public static Issue Warning(string path, string message) => new(IssueSeverity.Warning, path, message);

    public override string ToString() =>
        $"{Text.Get(Severity == IssueSeverity.Error ? "Issue_Error" : "Issue_Warning")}: {(Path.Length > 0 ? Path + ": " : "")}{Message}";
}

public static class IssueExtensions
{
    public static bool HasErrors(this IEnumerable<Issue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Error);
}
