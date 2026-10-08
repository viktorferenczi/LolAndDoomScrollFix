using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Limiter.Service;

/// <summary>
/// Az adatmappa (%ProgramData%\LolScrollLimiter): adatbázis és limits.json.
/// Szolgáltatásként indulva minden induláskor újra beállítja a jogosultságokat:
/// csak a SYSTEM és a rendszergazdák férnek hozzá, normál felhasználó se nem olvashatja, se nem írhatja.
/// </summary>
public sealed record DataPaths(string Directory, string DatabasePath, string SettingsPath)
{
    public static DataPaths Resolve()
    {
        var dir = Environment.GetEnvironmentVariable("LIMITER_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LolScrollLimiter");

        System.IO.Directory.CreateDirectory(dir);
        if (WindowsServiceHelpers.IsWindowsService()) Secure(dir);

        return new DataPaths(dir, Path.Combine(dir, "limiter.db"), Path.Combine(dir, "limits.json"));
    }

    private static void Secure(string dir)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        }
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(dir).SetAccessControl(security);

        // A meglévő fájlokról töröljük az explicit jogokat, így csak a mappától örökölt szabályok maradnak.
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
        {
            var fs = new FileSecurity();
            fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            file.SetAccessControl(fs);
        }
    }
}
