using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Limiter.Service;

/// <summary>
/// The data folder (%ProgramData%\LolScrollLimiter): the database and limits.json.
/// When started as a service it resets the permissions on every start:
/// only SYSTEM and administrators have access, a normal user can neither read nor write it.
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

        // The explicit rights are removed from the existing files, so only the rules inherited from the folder remain.
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
        {
            var fs = new FileSecurity();
            fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            file.SetAccessControl(fs);
        }
    }
}
