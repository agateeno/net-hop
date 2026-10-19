namespace NetHop.Core;

/// <summary>Well-known names shared by every NetHop component.</summary>
public static class NetHopConstants
{
    /// <summary>File extension of a profile document (YAML content).</summary>
    public const string ProfileExtension = ".nethop";

    /// <summary>File name of the automation rules document (YAML content).</summary>
    public const string RulesFileName = "rules.nethop.yml";

    /// <summary>URI scheme used for activation, e.g. <c>nethop://apply/Office</c>.</summary>
    public const string UriScheme = "nethop";

    /// <summary>Highest profile/rules schema version this build understands.</summary>
    public const int CurrentSchemaVersion = 1;
}
