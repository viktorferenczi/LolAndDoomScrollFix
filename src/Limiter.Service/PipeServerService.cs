using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Limiter.Protocol;

namespace Limiter.Service;

/// <summary>
/// Named pipe szerver a bővítmény (native host) és a tálcaalkalmazás felé.
/// A hívó Windows-felhasználóját az operációs rendszer azonosítja (impersonation), nem a kliens állítása.
/// </summary>
public sealed class PipeServerService(RequestHandler handler, ILogger<PipeServerService> log) : BackgroundService
{
    private const int MaxLineLength = 16 * 1024;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreatePipe(first);
                first = false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Más folyamat már létrehozta ezt a pipe-nevet (pl. megpróbálták „elfoglalni”).
                // A kliensek ilyenkor a szerver-ellenőrzés miatt hibát jeleznek; mi újrapróbáljuk.
                log.LogCritical(ex, "A pipe nem hozható létre — egy másik folyamat foglalja a nevet.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }
            catch (IOException ex)
            {
                log.LogError(ex, "A pipe nem hozható létre.");
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (IOException ex)
            {
                log.LogWarning(ex, "Kapcsolódási hiba a pipe-on.");
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(server, ct), ct);
        }
    }

    private static NamedPipeServerStream CreatePipe(bool firstInstance)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        var options = PipeOptions.Asynchronous;
        if (firstInstance) options |= PipeOptions.FirstPipeInstance;
        return NamedPipeServerStreamAcl.Create(IpcConstants.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 4096, 4096, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        string? sid = null;

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                // Az impersonation csak azután működik, hogy a kliens már írt a pipe-ba.
                sid ??= IdentifyClient(pipe);

                IpcResponse response;
                if (line.Length > MaxLineLength)
                {
                    response = IpcResponse.Failure(null, "Túl hosszú kérés.");
                }
                else
                {
                    IpcRequest? request = null;
                    try { request = IpcJson.Deserialize<IpcRequest>(line); }
                    catch (JsonException) { }

                    response = request is null
                        ? IpcResponse.Failure(null, "Érvénytelen kérés.")
                        : SafeHandle(sid, request);
                }

                await writer.WriteLineAsync(IpcJson.Serialize(response).AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* a kliens bontott */ }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Hiba a pipe-kliens kezelése közben.");
        }
    }

    private IpcResponse SafeHandle(string sid, IpcRequest request)
    {
        try
        {
            return handler.Handle(sid, request);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Hiba a kérés feldolgozásakor ({Type}).", request.Type);
            return IpcResponse.Failure(request.Id, "Belső hiba a szolgáltatásban.");
        }
    }

    private static string IdentifyClient(NamedPipeServerStream pipe)
    {
        string? sid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            sid = identity.User?.Value;
        });
        return sid ?? throw new UnauthorizedAccessException("A kliens felhasználója nem azonosítható.");
    }
}
