using System.IO.Pipes;
using System.Net;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetHop.Core.Networking;
using StreamJsonRpc;

namespace NetHop.Ipc;

public static class NetHopPipe
{
    /// <summary>Bump when <see cref="INetHopService"/> changes incompatibly.</summary>
    public const int ProtocolVersion = 1;

    public const string PipeName = "NetHop.Service.v1";

    /// <summary>The Windows service that must own the pipe (see <see cref="PipeServerCheck"/>).</summary>
    public const string ServiceName = "NetHop";

    /// <summary>Builds the JSON-RPC connection used by both ends of the pipe.</summary>
    public static JsonRpc CreateRpc(Stream stream)
    {
        var formatter = new SystemTextJsonFormatter();
        ConfigureJson(formatter.JsonSerializerOptions);
        return new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, formatter));
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new IPAddressJsonConverter());
        options.Converters.Add(new IpCidrJsonConverter());
    }
}

/// <summary>Client side: connects to the service and returns a typed proxy.</summary>
public sealed class NetHopClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly JsonRpc _rpc;

    private NetHopClient(NamedPipeClientStream pipe, JsonRpc rpc, INetHopService service)
    {
        _pipe = pipe;
        _rpc = rpc;
        Service = service;
    }

    public INetHopService Service { get; }

    /// <summary>Completes when the connection drops (service stopped or restarted).</summary>
    public Task Completion => _rpc.Completion;

    /// <exception cref="TimeoutException">The service is not running.</exception>
    /// <exception cref="UnauthorizedAccessException">The pipe refused the connection.</exception>
    /// <exception cref="UntrustedPipeServerException">The pipe is not served by the NetHop service.</exception>
    public static async Task<NetHopClient> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Identification lets the service read our token to check group membership, nothing more.
        var pipe = new NamedPipeClientStream(".", NetHopPipe.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            PipeServerCheck.EnsureServedByService(pipe);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var rpc = NetHopPipe.CreateRpc(pipe);
        var service = rpc.Attach<INetHopService>();
        rpc.StartListening();
        return new NetHopClient(pipe, rpc, service);
    }

    public async ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class IPAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        IPAddress.Parse(reader.GetString() ?? throw new JsonException("IP address expected."));

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class IpCidrJsonConverter : JsonConverter<IpCidr>
{
    public override IpCidr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        IpCidr.Parse(reader.GetString() ?? throw new JsonException("CIDR address expected."));

    public override void Write(Utf8JsonWriter writer, IpCidr value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
