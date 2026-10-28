using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Limiter.Core;

public enum PasswordFileState
{
    /// <summary>Nincs jelszó beállítva.</summary>
    Missing,

    Valid,

    /// <summary>A fájl megvan, de nem értelmezhető. Nem tekintjük „nincs jelszónak”: a védelem nem esik el némán.</summary>
    Corrupt,
}

/// <summary>A tárolt jelszó-ellenőrző (PBKDF2-SHA256); maga a jelszó sehol sem tárolódik.</summary>
public sealed record PasswordRecord
{
    public int Version { get; init; } = 1;
    public int Iterations { get; init; }
    public string Salt { get; init; } = "";
    public string Hash { get; init; } = "";
}

/// <summary>
/// Az admin jelszava: a telepítéskor adják meg, és az admin-felület megnyitásához, valamint az eltávolításhoz kell.
/// A fájl (admin.json) az adatmappában van, ahová csak a rendszergazdák és a SYSTEM fér hozzá.
/// </summary>
public static class AdminPassword
{
    public const int MinLength = 6;
    public const int MaxLength = 128;
    public const int DefaultIterations = 200_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MinIterations = 1_000;
    private const int MaxIterations = 5_000_000;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LolScrollLimiter", "admin.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Hibaszöveg, ha a jelszó nem fogadható el; különben null.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return Loc.T("Enter the password.", "Add meg a jelszót.");
        if (string.IsNullOrWhiteSpace(password)) return Loc.T("The password cannot consist of spaces only.", "A jelszó nem állhat csupa szóközből.");
        if (password.Length < MinLength) return Loc.T($"The password must be at least {MinLength} characters long.", $"A jelszó legalább {MinLength} karakter legyen.");
        if (password.Length > MaxLength) return Loc.T($"The password can be at most {MaxLength} characters long.", $"A jelszó legfeljebb {MaxLength} karakter lehet.");
        return null;
    }

    public static PasswordRecord Create(string password, int iterations = DefaultIterations)
    {
        var error = Validate(password);
        if (error is not null) throw new ArgumentException(error, nameof(password));
        if (iterations is < MinIterations or > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new PasswordRecord
        {
            Iterations = iterations,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt, iterations)),
        };
    }

    public static bool Verify(string? password, PasswordRecord record)
    {
        if (string.IsNullOrEmpty(password) || !IsWellFormed(record, out var salt, out var expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Derive(password, salt, record.Iterations), expected);
    }

    public static PasswordFileState Load(string path, out PasswordRecord? record)
    {
        record = null;
        try
        {
            if (!File.Exists(path)) return PasswordFileState.Missing;
            var parsed = JsonSerializer.Deserialize<PasswordRecord>(File.ReadAllText(path), Json);
            if (parsed is null || !IsWellFormed(parsed, out _, out _)) return PasswordFileState.Corrupt;
            record = parsed;
            return PasswordFileState.Valid;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return PasswordFileState.Corrupt;
        }
    }

    /// <summary>Egy lépésben cseréli a fájlt (ideiglenes fájl, majd átnevezés), így sosem marad félig megírt jelszófájl.</summary>
    public static void Save(string path, PasswordRecord record)
    {
        if (!IsWellFormed(record, out _, out _)) throw new ArgumentException("Invalid password verifier.", nameof(record));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Json), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>
    /// A telepítő a jelszót ideiglenes fájlban adja át: minden UTF-16 karakter négy hexa számjegy (így az ékezetes
    /// betűk és a kódlap sem okozhat eltérést). Érvénytelen bemenetnél null.
    /// </summary>
    public static string? DecodeTransfer(string? text)
    {
        var hex = (text ?? "").Trim();
        if (hex.Length == 0 || hex.Length % 4 != 0) return null;
        var chars = new char[hex.Length / 4];
        for (var i = 0; i < chars.Length; i++)
        {
            if (!ushort.TryParse(hex.AsSpan(i * 4, 4), System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out var code)) return null;
            chars[i] = (char)code;
        }
        return new string(chars);
    }

    private static bool IsWellFormed(PasswordRecord record, out byte[] salt, out byte[] hash)
    {
        salt = hash = [];
        if (record.Version != 1 || record.Iterations is < MinIterations or > MaxIterations) return false;
        try
        {
            salt = Convert.FromBase64String(record.Salt ?? "");
            hash = Convert.FromBase64String(record.Hash ?? "");
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length == SaltSize && hash.Length == HashSize;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)),
            salt, iterations, HashAlgorithmName.SHA256, HashSize);
}
