using System.Text.Json;
using NetHop.Core;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;
using NetHop.Core.Serialization;
using NetHop.Ipc;

namespace NetHop.Cli;

internal sealed class UsageException(string message) : Exception(message);

internal sealed class CommandFailedException(string message, object? result = null) : Exception(message)
{
    public object? Result { get; } = result;
}

internal static class Commands
{
    public const string HelpText = """
        NetHop - switch network profiles

        Usage: nethop <command> [arguments] [--json]

          list                        List profiles (* = active)
          status                      Show the active profile and automation state
          adapters                    List network adapters with their addresses
          apply <profile> [--dry-run] Apply a profile by name or id (--dry-run only shows the steps)
          enable <adapter>            Enable an adapter (name, GUID or MAC)
          disable <adapter>           Disable an adapter
          rename <adapter> <new name> Rename an adapter
          import <file.nethop>        Add or update a profile from a file
          export <profile> <file>     Write a profile to a file
          validate <file.nethop>      Check a profile file without the service
          pause [minutes]             Pause automations (default 60 minutes)
          resume                      Resume automations
          nethop://...                Run an activation URI (nethop://apply/Office)

        Exit codes: 0 ok, 1 failed, 2 usage error, 3 service not running, 4 access denied
        """;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static readonly HashSet<string> ServiceCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "status", "adapters", "apply", "enable", "disable", "rename", "import", "export", "pause", "resume",
    };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        NetHopPipe.ConfigureJson(options);
        return options;
    }

    public static async Task<int> RunAsync(string[] args, bool dryRun, bool json)
    {
        var command = args[0];

        // Commands that do not need the service.
        if (command.Equals("validate", StringComparison.OrdinalIgnoreCase))
            return Validate(Arg(args, 1, "file"), json);

        if (NetHopUri.TryParse(command, out var uri))
        {
            args = uri.Command switch
            {
                UriCommand.Apply => ["apply", uri.Profile!],
                _ => ["status"],
            };
            command = args[0];
        }

        if (!ServiceCommands.Contains(command))
            throw new UsageException($"Unknown command '{command}'.");

        await using var client = await NetHopClient.ConnectAsync(TimeSpan.FromSeconds(3));
        var service = client.Service;

        switch (command.ToLowerInvariant())
        {
            case "list":
                var profiles = await service.ListProfilesAsync();
                if (json)
                    return Print(profiles);
                foreach (var p in profiles)
                    Console.WriteLine($"{(p.IsActive ? "*" : " ")} {p.Name}");
                return 0;

            case "status":
                var status = await service.GetStatusAsync();
                if (json)
                    return Print(status);
                Console.WriteLine($"Active profile: {status.ActiveProfileName ?? "(none)"}{(status.ActiveProfileDrifted ? "  (changed since last apply)" : "")}");
                if (status.LastAppliedAt is { } at)
                    Console.WriteLine($"Last applied:   {at:g}");
                Console.WriteLine($"Automations:    {(status.AutomationsPausedUntil is { } until ? $"paused until {until:t}" : "active")}");
                if (status.SsidUnavailable)
                    Console.WriteLine("Warning: Wi-Fi names cannot be read. Turn on location access (Settings > Privacy & security > Location).");
                return 0;

            case "adapters":
                var network = await service.GetNetworkAsync();
                if (json)
                    return Print(network);
                foreach (var a in network.Adapters)
                    PrintAdapter(a);
                return 0;

            case "apply":
                return Report(await service.ApplyProfileAsync(Arg(args, 1, "profile"), dryRun), json);

            case "enable" or "disable":
                var adapter = await FindAdapterAsync(service, Arg(args, 1, "adapter"));
                await service.SetAdapterEnabledAsync(adapter.Id, command.Equals("enable", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"Adapter '{adapter.Name}' {command.ToLowerInvariant()}d.");
                return 0;

            case "rename":
                var target = await FindAdapterAsync(service, Arg(args, 1, "adapter"));
                var newName = string.Join(' ', args.Skip(2));
                if (newName.Length == 0)
                    throw new UsageException("Missing argument: new name.");
                await service.RenameAdapterAsync(target.Id, newName);
                Console.WriteLine($"Adapter '{target.Name}' renamed to '{newName}'.");
                return 0;

            case "import":
                var saved = await service.SaveProfileYamlAsync(await File.ReadAllTextAsync(Arg(args, 1, "file")));
                foreach (var issue in saved.Issues)
                    Console.Error.WriteLine(issue);
                if (!saved.Saved)
                    throw new CommandFailedException("The profile was not imported.", saved);
                Console.WriteLine("Profile imported.");
                return 0;

            case "export":
                var profile = (await service.ListProfilesAsync())
                    .FirstOrDefault(p => p.Name.Equals(Arg(args, 1, "profile"), StringComparison.CurrentCultureIgnoreCase) || p.Id.ToString() == args[1])
                    ?? throw new CommandFailedException($"Profile '{args[1]}' not found.");
                await File.WriteAllTextAsync(Arg(args, 2, "file"), await service.GetProfileYamlAsync(profile.Id));
                return 0;

            case "pause":
                var minutes = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 60;
                await service.PauseAutomationsAsync(minutes);
                Console.WriteLine($"Automations paused for {minutes} minutes.");
                return 0;

            case "resume":
                await service.PauseAutomationsAsync(0);
                Console.WriteLine("Automations resumed.");
                return 0;

            default:
                throw new UsageException($"Unknown command '{command}'.");
        }
    }

    private static int Validate(string path, bool json)
    {
        Profile profile;
        try
        {
            profile = ProfileSerializer.Load(path);
        }
        catch (NetHopFormatException ex)
        {
            throw new CommandFailedException($"{path}: {ex.Message}");
        }

        var issues = ProfileValidator.Validate(profile);
        if (json)
            Print(issues);
        else
        {
            foreach (var issue in issues)
                Console.WriteLine(issue);
            Console.WriteLine(issues.HasErrors() ? "Invalid profile." : $"Profile '{profile.Name}' is valid.");
        }
        return issues.HasErrors() ? 1 : 0;
    }

    private static int Report(ApplyResult result, bool json)
    {
        if (json)
        {
            Print(result);
            return result.Success ? 0 : 1;
        }

        foreach (var issue in result.Issues)
            Console.Error.WriteLine(issue);

        var verb = result.DryRun ? "Would run" : "Ran";
        if (result.Steps.Length == 0 && result.Success)
            Console.WriteLine(result.DryRun ? "Nothing to change." : $"'{result.ProfileName}' is already active.");
        else
            foreach (var step in result.Steps)
                Console.WriteLine($"  {verb}: {step}");

        if (!result.Success)
            throw new CommandFailedException($"Failed: {result.Error}{(result.RolledBack ? " (previous configuration restored)" : "")}");

        if (!result.DryRun)
            Console.WriteLine($"Profile '{result.ProfileName}' applied in {result.Duration.TotalSeconds:0.0}s.");
        return 0;
    }

    private static async Task<AdapterInfo> FindAdapterAsync(INetHopService service, string reference)
    {
        var adapters = (await service.GetNetworkAsync()).Adapters.Select(a => a.Info).ToList();
        var match = AdapterMatcher.Resolve(new AdapterMatch { Guid = reference, Mac = reference, Name = reference }, adapters);
        return match.Adapter ?? throw new CommandFailedException(match.Problem ?? $"Adapter '{reference}' not found.");
    }

    private static void PrintAdapter(AdapterSnapshot a)
    {
        var state = !a.Enabled ? "disabled" : a.Connected ? "connected" : "disconnected";
        Console.WriteLine($"{a.Info.Name}  [{state}]{(a.Ssid is null ? "" : $"  SSID: {a.Ssid}")}");
        Console.WriteLine($"  {a.Info.Description}  {a.Info.Mac}  {a.Info.Id:B}");
        foreach (var (label, family) in new[] { ("IPv4", a.Ipv4), ("IPv6", a.Ipv6) })
        {
            if (family is null)
                continue;
            Console.WriteLine($"  {label}: {(family.Dhcp ? "automatic" : "static")}  {string.Join(", ", family.AllAddresses)}");
            if (family.ManualGateways.Count > 0)
                Console.WriteLine($"    gateway: {string.Join(", ", family.ManualGateways)}");
            if (family.EffectiveDnsServers.Count > 0)
                Console.WriteLine($"    dns: {string.Join(", ", family.EffectiveDnsServers)}{(family.StaticDnsServers.Count > 0 ? " (static)" : "")}");
        }
        Console.WriteLine();
    }

    private static int Print<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        return 0;
    }

    private static string Arg(string[] args, int index, string name) =>
        args.Length > index ? args[index] : throw new UsageException($"Missing argument: {name}.");
}
