using System.Text.Json;
using NetHop.Cli;

// Exit codes are part of the contract for Power Automate and scripts.
const int Ok = 0, Failed = 1, Usage = 2, ServiceUnavailable = 3, AccessDenied = 4;

var json = args.Contains("--json");
var dryRun = args.Contains("--dry-run");
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

if (positional.Length == 0 || positional[0] is "help" or "-h" or "/?")
{
    Console.WriteLine(Commands.HelpText);
    return positional.Length == 0 ? Usage : Ok;
}

try
{
    return await Commands.RunAsync(positional, dryRun, json);
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run 'nethop help' for the list of commands.");
    return Usage;
}
catch (TimeoutException)
{
    // Off by default since 0.6: the app works without it, the command line does not.
    Console.Error.WriteLine("The NetHop service is not running. The command line needs it: turn it on in NetHop > Settings > NetHop service.");
    return ServiceUnavailable;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StreamJsonRpc.ConnectionLostException)
{
    Console.Error.WriteLine("The NetHop service refused the connection. Only administrator accounts can use NetHop.");
    return AccessDenied;
}
catch (StreamJsonRpc.RemoteInvocationException ex) when (NetHop.Ipc.NetHopErrors.IsReadOnly(ex))
{
    Console.Error.WriteLine("This account can only read NetHop's settings. Run the command as an administrator to make changes.");
    return AccessDenied;
}
catch (StreamJsonRpc.RemoteInvocationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return Failed;
}
catch (CommandFailedException ex)
{
    if (json && ex.Result is not null)
        Console.WriteLine(JsonSerializer.Serialize(ex.Result, Commands.JsonOptions));
    else
        Console.Error.WriteLine(ex.Message);
    return Failed;
}
