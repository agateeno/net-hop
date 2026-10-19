using NetHop.Core.Serialization;

namespace NetHop.Core.Profiles;

/// <summary>Reads and writes <c>.nethop</c> profile documents.</summary>
public static class ProfileSerializer
{
    public static string Serialize(Profile profile) => NetHopYaml.Serialize(profile);

    /// <exception cref="NetHopFormatException">The YAML is malformed or contains unknown keys.</exception>
    public static Profile Deserialize(string yaml) => NetHopYaml.Deserialize<Profile>(yaml);

    public static Profile Load(string path) => Deserialize(File.ReadAllText(path));

    public static void Save(Profile profile, string path)
    {
        // Write to a temp file first so a crash never leaves a half-written profile behind.
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(profile));
        File.Move(temp, path, overwrite: true);
    }
}
