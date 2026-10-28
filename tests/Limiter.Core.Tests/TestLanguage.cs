using System.Runtime.CompilerServices;

namespace Limiter.Core.Tests;

/// <summary>Tests run in English unless a test explicitly asks for another language (see <see cref="LocalizationTests"/>).</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Init() => Loc.Language = UiLanguage.English;
}
