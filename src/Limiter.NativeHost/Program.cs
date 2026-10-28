using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Limiter.Protocol;
using Limiter.Core;

// Chrome Native Messaging host: forwards the extension's messages (4-byte length + UTF-8 JSON) to the
// service's named pipe, and sends the response back. It has no logic of its own; if the service is not
// reachable it gives an error response, on which the extension lets everything through (fail-open) and reports an error.

const int MaxMessageBytes = 64 * 1024;
var timeout = TimeSpan.FromSeconds(3);

await using var stdin = Console.OpenStandardInput();
await using var stdout = Console.OpenStandardOutput();
IpcClient? client = null;

try
{
    while (await ReadMessageAsync(stdin) is { } message)
    {
        string? id = null;
        string response;
        try
        {
            var node = JsonNode.Parse(message)?.AsObject() ?? throw new FormatException("Nem JSON-objektum.");
            id = node["id"]?.GetValue<string>();
            // The source is set by the host, not by the web page/extension.
            node["source"] = IpcConstants.Sources.Extension;
            var line = node.ToJsonString();

            client ??= await IpcClient.ConnectAsync(timeout);
            response = await client.SendLineAsync(line, timeout);
        }
        catch (Exception ex)
        {
            client?.Dispose();
            client = null;
            response = IpcJson.Serialize(IpcResponse.Failure(id, Describe(ex)));
        }
        await WriteMessageAsync(stdout, response);
    }
}
finally
{
    client?.Dispose();
}
return 0;

static string Describe(Exception ex) => ex switch
{
    TimeoutException => Loc.T("The limiter service is not reachable (timeout).", "A korlátozó szolgáltatás nem érhető el (időtúllépés)."),
    UnauthorizedAccessException => Loc.T("The limiter service is not authentic: ", "A korlátozó szolgáltatás nem hiteles: ") + ex.Message,
    _ => Loc.T("The limiter service is not reachable: ", "A korlátozó szolgáltatás nem érhető el: ") + ex.Message,
};

static async Task<string?> ReadMessageAsync(Stream input)
{
    var header = new byte[4];
    if (!await ReadExactAsync(input, header)) return null;
    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length is < 0 or > MaxMessageBytes) throw new InvalidDataException("Invalid message length.");
    var body = new byte[length];
    if (!await ReadExactAsync(input, body)) return null;
    return Encoding.UTF8.GetString(body);
}

static async Task<bool> ReadExactAsync(Stream input, byte[] buffer)
{
    var read = 0;
    while (read < buffer.Length)
    {
        var n = await input.ReadAsync(buffer.AsMemory(read));
        if (n == 0) return false;
        read += n;
    }
    return true;
}

static async Task WriteMessageAsync(Stream output, string json)
{
    var body = Encoding.UTF8.GetBytes(json);
    var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
    await output.WriteAsync(header);
    await output.WriteAsync(body);
    await output.FlushAsync();
}
