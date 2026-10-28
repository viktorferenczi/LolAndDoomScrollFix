using System.Globalization;
using Microsoft.Win32;

namespace Limiter.Core;

public enum UiLanguage
{
    English,
    Hungarian,
}

/// <summary>
/// User-facing text in English and Hungarian. Both texts are passed at the call site
/// (<c>Loc.T("Save", "Mentés")</c>), so a translation can never be missing. The language is the one the installer
/// recorded in <c>HKLM\SOFTWARE\LolScrollLimiter\Language</c> ("en" or "hu"); without it, the Windows display language
/// decides (Hungarian for hu-*, English for everything else).
/// </summary>
public static class Loc
{
    public const string RegistryKey = @"SOFTWARE\LolScrollLimiter";
    public const string RegistryValue = "Language";

    private static UiLanguage? _override;
    private static readonly Lazy<UiLanguage> Detected = new(Detect);

    /// <summary>The active language. Settable for tests and for tools that want to force a language.</summary>
    public static UiLanguage Language
    {
        get => _override ?? Detected.Value;
        set => _override = value;
    }

    public static bool IsHungarian => Language == UiLanguage.Hungarian;

    public static string T(string english, string hungarian) => IsHungarian ? hungarian : english;

    /// <summary>The culture to format dates and numbers for the active language.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsHungarian ? "hu-HU" : "en-US");

    public static UiLanguage Parse(string? code) =>
        string.Equals(code?.Trim(), "hu", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Hungarian : UiLanguage.English;

    private static UiLanguage Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
                if (key?.GetValue(RegistryValue) is string code && code.Trim().Length > 0)
                    return Parse(code);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Falls back to the display language.
            }
        }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("hu", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Hungarian
            : UiLanguage.English;
    }
}
