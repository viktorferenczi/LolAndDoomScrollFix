using System.Reflection;

namespace Limiter.Core.Tests;

public class LimitSettingsEqualityTests
{
    [Fact]
    public void Equal_settings_with_equal_grant_lists_are_equal()
    {
        var a = LimitSettings.Default with { ExtraGrants = [new DayGrant { Date = new DateOnly(2026, 10, 5), LolMatches = 1 }] };
        var b = LimitSettings.Default with { ExtraGrants = [new DayGrant { Date = new DateOnly(2026, 10, 5), LolMatches = 1 }] };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Safeguard: if someone adds a new field to LimitSettings but forgets it in the equality check, this fails.</summary>
    [Fact]
    public void Every_property_takes_part_in_equality()
    {
        var baseline = LimitSettings.Default with { ExtraGrants = [new DayGrant { Date = new DateOnly(2026, 10, 5), LolMatches = 1 }] };
        foreach (var p in typeof(LimitSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.SetMethod is not null))
        {
            var changed = baseline with { };
            p.SetValue(changed, Alter(p.GetValue(baseline), p.PropertyType));
            Assert.False(baseline.Equals(changed), $"The field {p.Name} is not part of the equality check.");
        }
    }

    private static object? Alter(object? value, Type type) => value switch
    {
        int i => i + 1,
        bool b => !b,
        ResetMode m => m == ResetMode.Daily ? ResetMode.Rolling : ResetMode.Daily,
        BannedHours h => h with { Enabled = !h.Enabled },
        IReadOnlyList<DayGrant> list => list.Count == 0
            ? new List<DayGrant> { new() { Date = new DateOnly(2026, 1, 1), LolMatches = 1 } }
            : new List<DayGrant> { list[0] with { LolMatches = list[0].LolMatches + 1 } },
        _ => throw new NotSupportedException($"Unknown field type: {type}"),
    };
}
