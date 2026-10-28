using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Limiter.Core;

namespace Limiter.Protocol;

/// <summary>
/// A line-based client for the service's named pipe. After connecting it verifies that on the other side
/// the installed service really runs (session 0, a process started from the install folder),
/// so a pipe name "taken" by a normal user cannot return a fake limit.
/// </summary>
public sealed class IpcClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    private IpcClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public bool IsConnected => _pipe.IsConnected;

    public static async Task<IpcClient> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            ServerVerifier.EnsureTrustedServer(pipe);
            return new IpcClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends one raw JSON line and returns the response line.</summary>
    public async Task<string> SendLineAsync(string jsonLine, TimeSpan timeout, CancellationToken ct = default)
    {
        if (jsonLine.Contains('\n')) throw new ArgumentException(Loc.T("A request cannot contain a line break.", "A kérés nem tartalmazhat sortörést."), nameof(jsonLine));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await _writer.WriteLineAsync(jsonLine.AsMemory(), cts.Token).ConfigureAwait(false);
        var line = await _reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        return line ?? throw new IOException(Loc.T("The service closed the connection.", "A szolgáltatás bontotta a kapcsolatot."));
    }

    public async Task<IpcResponse> SendAsync(IpcRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        var line = await SendLineAsync(IpcJson.Serialize(request), timeout, ct).ConfigureAwait(false);
        return IpcJson.Deserialize<IpcResponse>(line) ?? throw new IOException(Loc.T("Empty response from the service.", "Üres válasz a szolgáltatástól."));
    }

    /// <summary>A one-off request over its own connection (e.g. for the tray application's periodic polling).</summary>
    public static async Task<IpcResponse> RequestAsync(IpcRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        using var client = await ConnectAsync(timeout, ct).ConfigureAwait(false);
        return await client.SendAsync(request, timeout, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
    }
}
