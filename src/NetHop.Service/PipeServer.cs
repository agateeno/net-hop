using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using NetHop.Host;
using NetHop.Ipc;

namespace NetHop.Service;

/// <summary>Accepts JSON-RPC clients on the NetHop pipe. Each connection gets its own JsonRpc over the shared controller.</summary>
public sealed class PipeServer(NetHopController controller, ILogger<PipeServer> logger) : BackgroundService
{
    // Any interactive user may connect (non-administrators read-only): cap the connections a SYSTEM service keeps.
    private const int MaxClients = 32;

    private readonly SemaphoreSlim _slots = new(MaxClients, MaxClients);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _slots.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(first);
                first = false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // FirstPipeInstance failed: someone else owns the pipe name. Refuse to run next to an impostor.
                logger.LogCritical(ex, "The pipe {Pipe} already exists; another process may be impersonating NetHop", NetHopPipe.PipeName);
                throw;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }

            _ = HandleClientAsync(pipe, stoppingToken).ContinueWith(_ => _slots.Release(), TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            string? user = null;
            bool administrator;
            try
            {
                // Read the client process's token rather than impersonating: ImpersonateNamedPipeClient
                // fails (ERROR_CANNOT_IMPERSONATE) until data has been read from the pipe.
                using var identity = ClientAuthorization.GetClientIdentity(pipe);
                user = identity.Name;
                if (ClientAuthorization.IsBelowMediumIntegrity(identity))
                {
                    logger.LogWarning("Refused a sandboxed (low integrity) client running as {User}", user);
                    return;
                }
                administrator = ClientAuthorization.IsAdministratorAccount(identity);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Refused a pipe client that could not be identified");
                return;
            }

            // Accounts that are not administrators may look but not change anything.
            using var rpc = NetHopPipe.CreateRpc(pipe);
            rpc.AddLocalRpcTarget<INetHopService>(administrator ? controller : new ReadOnlyService(controller), null);
            rpc.StartListening();
            logger.LogDebug("Client connected: {User} ({Access})", user, administrator ? "full access" : "read-only");

            try
            {
                await rpc.Completion.WaitAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
            }
            logger.LogDebug("Client disconnected: {User}", user);
        }
    }

    private static NamedPipeServerStream CreatePipe(bool firstInstance)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        // Non-elevated processes (tray app, Logi plugin) connect as interactive users; ClientAuthorization
        // then gives full access to administrators and a read-only connection to everyone else.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        var options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(NetHopPipe.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
    }
}
