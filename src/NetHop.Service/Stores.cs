using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using NetHop.Core;
using NetHop.Core.Profiles;
using NetHop.Core.Rules;
using NetHop.Core.Serialization;

namespace NetHop.Service;

/// <summary>Everything NetHop stores lives under %ProgramData%\NetHop, writable only by Administrators and SYSTEM.</summary>
public sealed class ServicePaths
{
    public ServicePaths(string programData)
    {
        Root = Path.Combine(programData, "NetHop");
        Profiles = Path.Combine(Root, "profiles");
        Rules = Path.Combine(Root, NetHopConstants.RulesFileName);
        State = Path.Combine(Root, "state.json");

        CreateProtected(Root);
        Directory.CreateDirectory(Profiles);
    }

    public string Root { get; }

    public string Profiles { get; }

    public string Rules { get; }

    public string State { get; }

    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>
    /// Creates the folder, or takes over an existing one, so that only SYSTEM and Administrators can change it.
    /// Standard users may create folders in %ProgramData%: one could create NetHop before the service does (as a
    /// junction, or owning files inside) and so write the rules and profiles the service runs.
    /// </summary>
    private static void CreateProtected(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists && directory.LinkTarget is not null)
        {
            directory.Delete(); // removes the link itself, never its target
            directory.Refresh();
        }
        else if (directory.Exists && !HasTrustedOwner(directory.GetAccessControl()))
        {
            // Not created by NetHop or an administrator: keep it for inspection, start from a clean folder.
            directory.MoveTo($"{path}.untrusted-{DateTime.UtcNow:yyyyMMddHHmmss}");
            directory = new DirectoryInfo(path);
        }

        var security = new DirectorySecurity();
        security.SetOwner(AdministratorsSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { SystemSid, AdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        if (!directory.Exists)
        {
            directory.Create(security);
            return;
        }

        directory.SetAccessControl(security);
        RepairContents(directory);
    }

    /// <summary>Removes links and gives every file and folder inside the folder's own owner and permissions.</summary>
    private static void RepairContents(DirectoryInfo root)
    {
        foreach (var entry in root.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
                continue;
            }

            if (entry is DirectoryInfo folder)
            {
                var security = new DirectorySecurity();
                security.SetOwner(AdministratorsSid);
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                folder.SetAccessControl(security);
                RepairContents(folder);
            }
            else
            {
                var security = new FileSecurity();
                security.SetOwner(AdministratorsSid);
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                ((FileInfo)entry).SetAccessControl(security);
            }
        }
    }

    private static bool HasTrustedOwner(DirectorySecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && (owner == SystemSid || owner == AdministratorsSid);
}

/// <summary>The <c>.nethop</c> files in the profiles folder, indexed by profile id.</summary>
public sealed class ProfileStore(ServicePaths paths, ILogger<ProfileStore> logger)
{
    private readonly Lock _lock = new();
    private Dictionary<Guid, (Profile Profile, string Path)>? _cache;

    public IReadOnlyList<Profile> All()
    {
        lock (_lock)
            return Load().Values.Select(v => v.Profile).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Profile? Find(string idOrName)
    {
        var all = All();
        return Guid.TryParse(idOrName, out var id)
            ? all.FirstOrDefault(p => p.Id == id)
            : all.FirstOrDefault(p => string.Equals(p.Name, idOrName.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public string? GetPath(Guid id)
    {
        lock (_lock)
            return Load().TryGetValue(id, out var entry) ? entry.Path : null;
    }

    public void Save(Profile profile)
    {
        lock (_lock)
        {
            var cache = Load();
            var duplicate = cache.Values.FirstOrDefault(v => v.Profile.Id != profile.Id
                && string.Equals(v.Profile.Name, profile.Name, StringComparison.CurrentCultureIgnoreCase));
            if (duplicate.Profile is not null)
                throw new InvalidOperationException($"Another profile is already named '{profile.Name}'.");

            var path = Path.Combine(paths.Profiles, FileNameFor(profile.Name) + NetHopConstants.ProfileExtension);
            ProfileSerializer.Save(profile, path);

            // Renamed profile: drop the file with the old name.
            if (cache.TryGetValue(profile.Id, out var old) && !string.Equals(old.Path, path, StringComparison.OrdinalIgnoreCase))
                File.Delete(old.Path);

            cache[profile.Id] = (profile, path);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var cache = Load();
            if (!cache.Remove(id, out var entry))
                return false;
            File.Delete(entry.Path);
            return true;
        }
    }

    private Dictionary<Guid, (Profile Profile, string Path)> Load()
    {
        if (_cache is not null)
            return _cache;

        _cache = [];
        foreach (var path in Directory.EnumerateFiles(paths.Profiles, "*" + NetHopConstants.ProfileExtension))
        {
            try
            {
                var profile = ProfileSerializer.Load(path);
                if (!_cache.TryAdd(profile.Id, (profile, path)))
                    logger.LogWarning("Skipping {Path}: duplicate profile id {Id}", path, profile.Id);
            }
            catch (Exception ex) when (ex is NetHopFormatException or IOException)
            {
                logger.LogWarning(ex, "Skipping unreadable profile {Path}", path);
            }
        }
        return _cache;
    }

    private static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "profile" : clean;
    }
}

public sealed class RuleStore(ServicePaths paths, ILogger<RuleStore> logger)
{
    public RuleSet Load()
    {
        if (!File.Exists(paths.Rules))
            return new RuleSet();
        try
        {
            return RuleSetSerializer.Deserialize(File.ReadAllText(paths.Rules));
        }
        catch (NetHopFormatException ex)
        {
            logger.LogError(ex, "Rules file is invalid, automations are disabled until it is fixed");
            return new RuleSet();
        }
    }

    public string LoadYaml() => File.Exists(paths.Rules) ? File.ReadAllText(paths.Rules) : RuleSetSerializer.Serialize(new RuleSet());

    public void Save(string yaml)
    {
        var temp = paths.Rules + ".tmp";
        File.WriteAllText(temp, yaml);
        File.Move(temp, paths.Rules, overwrite: true);
    }
}

/// <summary>Small piece of state that must survive a restart: which profile was applied last.</summary>
public sealed class StateStore(ServicePaths paths)
{
    public sealed record State(Guid? LastAppliedProfileId, DateTimeOffset? LastAppliedAt);

    public State Load()
    {
        try
        {
            return File.Exists(paths.State)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(paths.State)) ?? new State(null, null)
                : new State(null, null);
        }
        catch (JsonException)
        {
            return new State(null, null);
        }
    }

    public void Save(State state) => File.WriteAllText(paths.State, JsonSerializer.Serialize(state));
}
