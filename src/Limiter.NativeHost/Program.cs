using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Limiter.Protocol;

// Chrome Native Messaging host: a bővítmény üzeneteit (4 bájtos hossz + UTF-8 JSON) továbbítja a
// szolgáltatás named pipe-jára, és visszaküldi a választ. Saját logikája nincs; a szolgáltatás nem
// érhetősége esetén hibaválaszt ad, amelyre a bővítmény enged (fail-open) és hibát jelez.

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
            // A forrást a host állítja be, nem a weboldal/bővítmény.
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
    TimeoutException => "A korlátozó szolgáltatás nem érhető el (időtúllépés).",
    UnauthorizedAccessException => "A korlátozó szolgáltatás nem hiteles: " + ex.Message,
    _ => "A korlátozó szolgáltatás nem érhető el: " + ex.Message,
};

static async Task<string?> ReadMessageAsync(Stream input)
{
    var header = new byte[4];
    if (!await ReadExactAsync(input, header)) return null;
    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length is < 0 or > MaxMessageBytes) throw new InvalidDataException("Érvénytelen üzenethossz.");
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
