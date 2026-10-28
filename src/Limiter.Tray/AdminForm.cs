using System.Drawing;
using System.Globalization;
using Limiter.Core;

namespace Limiter.Tray;

/// <summary>
/// Admin-felület: a telepítéskor megadott beállítások (visszaállítás, keretek, hétvége, pihenőnap-jutalom,
/// tiltott órák) szerkesztése. Csak emelt (rendszergazdai) jogosultsággal fut, mert a limits.json az adatmappában
/// csak a rendszergazdáknak írható. A szolgáltatás 30 másodpercen belül átveszi a mentett értékeket.
/// </summary>
public sealed class AdminForm : Form
{
    private readonly string _path;

    private readonly RadioButton _rolling = new() { Text = "Gördülő 24 óra (minden felhasználás 24 órával később szabadul fel)", AutoSize = true };
    private readonly RadioButton _daily = new() { Text = "Naponta nullázás ebben az órában:", AutoSize = true };
    private readonly ComboBox _resetHour = Hours();

    private readonly NumericUpDown _lolMatches = Number(0, 1000);
    private readonly NumericUpDown _shortMinutes = Number(0, 1440);
    private readonly NumericUpDown _feedMinutes = Number(0, 1440);
    private readonly CheckBox _weekends = new() { Text = "A korlátok hétvégén (szombaton és vasárnap) is érvényesek", AutoSize = true };

    private readonly NumericUpDown _lolBonus = Number(0, 10);
    private readonly NumericUpDown _shortBonus = Number(0, 1440);
    private readonly NumericUpDown _feedBonus = Number(0, 1440);

    // Extra keret egy kiválasztott napra.
    private static readonly CultureInfo Hu = new("hu-HU");
    private readonly ComboBox _grantDay = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, DrawMode = DrawMode.OwnerDrawFixed, MaxDropDownItems = 16 };
    private readonly NumericUpDown _grantLol = Number(0, DayGrant.MaxMatches);
    private readonly NumericUpDown _grantShort = Number(0, DayGrant.MaxMinutes);
    private readonly NumericUpDown _grantFeed = Number(0, DayGrant.MaxMinutes);
    private readonly ListBox _grantList = new() { Width = 540, Height = 84, IntegralHeight = false };
    private readonly Label _grantNote = new() { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText };
    private List<DayGrant> _grants = [];
    private int _lastSelectableDay;

    private readonly BanRow _lolBan = new("LoL PvP-meccsek");
    private readonly BanRow _shortBan = new("Rövid videók");
    private readonly BanRow _feedBan = new("Facebook-hírfolyam");

    public AdminForm(string path)
    {
        _path = path;
        Text = "Admin beállítások — LoL- és görgetéskorlátozó";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(14);
        ClientSize = new Size(620, Math.Min(760, Screen.PrimaryScreen is { } screen ? screen.WorkingArea.Height - 90 : 700));

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Rendszergazdai módban vagy. A mentett értékeket a szolgáltatás legkésőbb 30 másodpercen belül átveszi; " +
                   "a már rögzített használat az új szabály szerint számít.",
            Margin = new Padding(0, 0, 0, 8),
        };

        root.Controls.Add(info);
        root.Controls.Add(Group("A keretek visszaállítása", ResetPanel()));
        root.Controls.Add(Group("Keretek", Grid(
            ("LoL PvP-meccsek száma:", _lolMatches, "meccs"),
            ("Rövid videók (Reels, TikTok, Shorts):", _shortMinutes, "perc"),
            ("Facebook kezdőlapi hírfolyam:", _feedMinutes, "perc")), _weekends));
        root.Controls.Add(Group("Pihenőnap-jutalom (üres nap után másnap, nem halmozódik; 0 = kikapcsolva)", Grid(
            ("LoL PvP-meccsek:", _lolBonus, "meccs"),
            ("Rövid videók:", _shortBonus, "perc"),
            ("Facebook kezdőlapi hírfolyam:", _feedBonus, "perc"))));
        root.Controls.Add(Group("Tiltott órák (a megadott órák között nem használható; a keretet nem érinti)",
            BanGrid(_lolBan, _shortBan, _feedBan)));
        root.Controls.Add(Group("Extra keret egy kiválasztott napra", GrantPanel()));

        var changePassword = new Button { Text = "Jelszó módosítása…", AutoSize = true };
        changePassword.Click += (_, _) => ChangePassword();
        var save = new Button { Text = "Mentés", AutoSize = true, DialogResult = DialogResult.None, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Mégse", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 0) };
        save.Click += (_, _) => Save();
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.AddRange([cancel, save, changePassword]);

        scroll.Controls.Add(root);
        Controls.Add(scroll);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        _rolling.CheckedChanged += (_, _) => { _resetHour.Enabled = _daily.Checked; RefreshGrantDays(); };
        _daily.CheckedChanged += (_, _) => { _resetHour.Enabled = _daily.Checked; RefreshGrantDays(); };
        _resetHour.SelectedIndexChanged += (_, _) => RefreshGrantDays();
        _weekends.CheckedChanged += (_, _) => RefreshGrantDays();
        _grantDay.DrawItem += DrawGrantDay;
        _grantDay.SelectionChangeCommitted += (_, _) => OnGrantDayChosen();
        _grantList.SelectedIndexChanged += (_, _) => OnGrantListChosen();

        LoadFromFile();
    }

    // ---------- betöltés / mentés ----------

    private void LoadFromFile()
    {
        var (s, warning) = SettingsFile.Load(_path);
        if (warning is not null) MessageBox.Show(this, warning, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        _rolling.Checked = s.ResetMode == ResetMode.Rolling;
        _daily.Checked = s.ResetMode == ResetMode.Daily;
        _resetHour.SelectedIndex = s.DailyResetHour;
        _resetHour.Enabled = _daily.Checked;

        _lolMatches.Value = s.LolPvpMatches;
        _shortMinutes.Value = s.ShortVideoMinutes;
        _feedMinutes.Value = s.FacebookFeedMinutes;
        _weekends.Checked = s.EnforceOnWeekends;

        _lolBonus.Value = s.LolRestDayBonusMatches;
        _shortBonus.Value = s.ShortVideoRestDayBonusMinutes;
        _feedBonus.Value = s.FacebookFeedRestDayBonusMinutes;

        _lolBan.Set(s.LolBannedHours);
        _shortBan.Set(s.ShortVideoBannedHours);
        _feedBan.Set(s.FacebookFeedBannedHours);

        // A lejárt (múltbeli) extráknak nincs hatásuk, ezért nem is jelennek meg; mentéskor eltűnnek a fájlból.
        _grants = [.. GrantDays.Pruned(s, DateTimeOffset.UtcNow, TimeZoneInfo.Local).ExtraGrants];
        RefreshGrantList();
        RefreshGrantDays();
    }

    private LimitSettings Collect() => new()
    {
        ResetMode = _daily.Checked ? ResetMode.Daily : ResetMode.Rolling,
        DailyResetHour = _resetHour.SelectedIndex < 0 ? 3 : _resetHour.SelectedIndex,
        LolPvpMatches = (int)_lolMatches.Value,
        ShortVideoMinutes = (int)_shortMinutes.Value,
        FacebookFeedMinutes = (int)_feedMinutes.Value,
        EnforceOnWeekends = _weekends.Checked,
        LolRestDayBonusMatches = (int)_lolBonus.Value,
        ShortVideoRestDayBonusMinutes = (int)_shortBonus.Value,
        FacebookFeedRestDayBonusMinutes = (int)_feedBonus.Value,
        LolBannedHours = _lolBan.Get(),
        ShortVideoBannedHours = _shortBan.Get(),
        FacebookFeedBannedHours = _feedBan.Get(),
        ExtraGrants = _grants,
    };

    private void Save()
    {
        var settings = GrantDays.Pruned(Collect(), DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        var errors = SettingsFile.Validate(settings, TimeZoneInfo.Local);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SettingsFile.Save(_path, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "A beállítások mentése nem sikerült:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this, "A beállítások mentve. A szolgáltatás 30 másodpercen belül átveszi őket.", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ChangePassword()
    {
        if (AdminGate.SetNew(this, prompt: "Add meg az új admin jelszót."))
            MessageBox.Show(this, "Az admin jelszó megváltozott.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------- extra keret egy napra ----------

    private Control GrantPanel()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        panel.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 6),
            Text = "Az extra a nap alapkeretéhez és a pihenőnap-jutalomhoz adódik, csak azon az egy napon. " +
                   "A tiltott órákat nem írja felül. A „nap” napi nullázásnál a nullázás órájától a következőig tart, gördülő 24 óránál éjféltől éjfélig.",
        });
        panel.Controls.Add(new Label { Text = "Nap:", AutoSize = true });
        panel.Controls.Add(_grantDay);
        _grantNote.Margin = new Padding(0, 2, 0, 6);
        panel.Controls.Add(_grantNote);

        panel.Controls.Add(Grid(
            ("LoL PvP-meccsek:", _grantLol, "extra meccs"),
            ("Rövid videók:", _grantShort, "extra perc"),
            ("Facebook kezdőlapi hírfolyam:", _grantFeed, "extra perc")));

        var set = new Button { Text = "Extra mentése a napra", AutoSize = true };
        var remove = new Button { Text = "Extra törlése a napról", AutoSize = true };
        set.Click += (_, _) => SetGrant();
        remove.Click += (_, _) => RemoveGrant();
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 6) };
        row.Controls.AddRange([set, remove]);
        panel.Controls.Add(row);

        panel.Controls.Add(new Label { Text = "Megadott extrák (a Mentés gombbal lépnek érvénybe):", AutoSize = true });
        panel.Controls.Add(_grantList);
        return panel;
    }

    private sealed record DayItem(GrantDayOption Option)
    {
        public override string ToString()
        {
            var text = Option.Date.ToDateTime(TimeOnly.MinValue).ToString("yyyy. MM. dd., dddd", Hu);
            if (Option.IsToday) text += " — most tartó nap";
            if (!Option.Selectable) text += " — nem választható: hétvége, nincs korlát";
            else if (Option.HasGrant) text += " — van extra";
            return text;
        }
    }

    /// <summary>A beállításokból (az űrlap mostani állása szerint) újraépíti a választható napok listáját.</summary>
    private void RefreshGrantDays()
    {
        var current = _grantDay.SelectedItem is DayItem item ? item.Option.Date : (DateOnly?)null;
        var settings = Collect();
        var options = GrantDays.Options(settings, DateTimeOffset.UtcNow, TimeZoneInfo.Local);

        _grantDay.BeginUpdate();
        _grantDay.Items.Clear();
        foreach (var o in options) _grantDay.Items.Add(new DayItem(o));
        _grantDay.EndUpdate();

        // Marad a korábbi nap, ha még választható; különben az első választható nap.
        var index = options.ToList().FindIndex(o => o.Selectable && o.Date == current);
        if (index < 0) index = options.ToList().FindIndex(o => o.Selectable);
        _grantDay.SelectedIndex = index; // -1, ha nincs választható nap
        _lastSelectableDay = index;
        LoadGrantValues();
        RefreshGrantList();

        var weekendGrants = _grants.Count(g => GrantDays.Check(settings, g.Date, TimeZoneInfo.Local) == GrantDayStatus.ExemptWeekend);
        _grantNote.Text = weekendGrants > 0
            ? $"Figyelem: {weekendGrants} extra hétvégére esik, ahol nincs korlát. Töröld őket, vagy kapcsold be a hétvégi korlátot, különben a mentés hibát jelez."
            : index < 0
                ? "Nincs választható nap."
                : "A szürke napok nem választhatók: hétvégén nincs korlát (hacsak a hétvégi korlát nincs bekapcsolva), ezért ott az extra felesleges.";
        _grantNote.ForeColor = weekendGrants > 0 ? Color.Firebrick : SystemColors.GrayText;
    }

    private void DrawGrantDay(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var item = (DayItem)_grantDay.Items[e.Index]!;
        e.DrawBackground();
        var color = item.Option.Selectable ? e.ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(e.Graphics, item.ToString(), e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        e.DrawFocusRectangle();
    }

    private void OnGrantDayChosen()
    {
        if (_grantDay.SelectedItem is DayItem { Option.Selectable: false })
        {
            // Tiltott nap: visszaállunk az előzőre.
            _grantDay.SelectedIndex = _lastSelectableDay;
            MessageBox.Show(this, "Erre a napra nem adható extra: hétvégén nincs korlát. Kapcsold be a hétvégi korlátot, ha szükséged van rá.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _lastSelectableDay = _grantDay.SelectedIndex;
        LoadGrantValues();
    }

    /// <summary>A kiválasztott nap meglévő extráját beírja a mezőkbe (ha nincs, nullák).</summary>
    private void LoadGrantValues()
    {
        var existing = _grantDay.SelectedItem is DayItem item ? _grants.FirstOrDefault(g => g.Date == item.Option.Date) : null;
        _grantLol.Value = existing?.LolMatches ?? 0;
        _grantShort.Value = existing?.ShortVideoMinutes ?? 0;
        _grantFeed.Value = existing?.FacebookFeedMinutes ?? 0;
    }

    private void SetGrant()
    {
        if (_grantDay.SelectedItem is not DayItem { Option.Selectable: true } item)
        {
            MessageBox.Show(this, "Válassz egy napot.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var grant = new DayGrant
        {
            Date = item.Option.Date,
            LolMatches = (int)_grantLol.Value,
            ShortVideoMinutes = (int)_grantShort.Value,
            FacebookFeedMinutes = (int)_grantFeed.Value,
        };
        if (grant.IsEmpty)
        {
            MessageBox.Show(this, "Adj meg legalább egy értéket (meccs vagy perc). Meglévő extra eltávolításához használd a törlés gombot.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _grants = [.. _grants.Where(g => g.Date != grant.Date).Append(grant).OrderBy(g => g.Date)];
        RefreshGrantDays();
    }

    private void RemoveGrant()
    {
        if (_grantDay.SelectedItem is not DayItem item || _grants.All(g => g.Date != item.Option.Date))
        {
            MessageBox.Show(this, "A kiválasztott napra nincs extra.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _grants = [.. _grants.Where(g => g.Date != item.Option.Date)];
        RefreshGrantDays();
    }

    private void RefreshGrantList()
    {
        _grantList.BeginUpdate();
        _grantList.Items.Clear();
        foreach (var g in _grants)
        {
            var parts = new List<string>();
            if (g.LolMatches > 0) parts.Add($"LoL +{g.LolMatches} meccs");
            if (g.ShortVideoMinutes > 0) parts.Add($"rövid videó +{g.ShortVideoMinutes} perc");
            if (g.FacebookFeedMinutes > 0) parts.Add($"hírfolyam +{g.FacebookFeedMinutes} perc");
            _grantList.Items.Add($"{g.Date.ToDateTime(TimeOnly.MinValue).ToString("yyyy. MM. dd., dddd", Hu)}: {string.Join(", ", parts)}");
        }
        _grantList.EndUpdate();
    }

    /// <summary>A listából választott extra napját a napválasztóban is kijelöli (ha az a nap választható).</summary>
    private void OnGrantListChosen()
    {
        var i = _grantList.SelectedIndex;
        if (i < 0 || i >= _grants.Count) return;
        for (var k = 0; k < _grantDay.Items.Count; k++)
        {
            if (_grantDay.Items[k] is DayItem { Option.Selectable: true } item && item.Option.Date == _grants[i].Date)
            {
                _grantDay.SelectedIndex = k;
                _lastSelectableDay = k;
                LoadGrantValues();
                return;
            }
        }
    }

    // ---------- felépítés ----------

    private Control ResetPanel()
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.AddRange([_daily, _resetHour]);
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.AddRange([_rolling, row]);
        return panel;
    }

    private static GroupBox Group(string title, Control content, Control? extra = null)
    {
        var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 8), MinimumSize = new Size(560, 0) };
        var inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top };
        inner.Controls.Add(content);
        if (extra is not null) inner.Controls.Add(extra);
        group.Controls.Add(inner);
        return group;
    }

    private static TableLayoutPanel Grid(params (string Label, NumericUpDown Input, string Unit)[] rows)
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        foreach (var (label, input, unit) in rows)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) });
            input.Margin = new Padding(0, 2, 6, 2);
            table.Controls.Add(input);
            table.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left });
        }
        return table;
    }

    private static TableLayoutPanel BanGrid(params BanRow[] rows)
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
        table.Controls.Add(new Label { Text = "", AutoSize = true });
        table.Controls.Add(new Label { Text = "kezdete", AutoSize = true });
        table.Controls.Add(new Label { Text = "", AutoSize = true });
        table.Controls.Add(new Label { Text = "vége", AutoSize = true });
        foreach (var row in rows) row.AddTo(table);
        return table;
    }

    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 70, ThousandsSeparator = false };

    private static ComboBox Hours()
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        for (var h = 0; h < 24; h++) box.Items.Add($"{h:00}:00");
        box.SelectedIndex = 3;
        return box;
    }

    /// <summary>Egy tiltott időszak sora: jelölőnégyzet, kezdő és záró óra.</summary>
    private sealed class BanRow
    {
        private readonly CheckBox _enabled;
        private readonly ComboBox _from = Hours();
        private readonly ComboBox _to = Hours();

        public BanRow(string caption)
        {
            _enabled = new CheckBox { Text = caption, AutoSize = true, Width = 190, Anchor = AnchorStyles.Left };
            _from.SelectedIndex = 1;
            _to.SelectedIndex = 5;
            _enabled.CheckedChanged += (_, _) => Sync();
            Sync();
        }

        private void Sync() => _from.Enabled = _to.Enabled = _enabled.Checked;

        public void AddTo(TableLayoutPanel table)
        {
            table.Controls.Add(_enabled);
            table.Controls.Add(_from);
            table.Controls.Add(new Label { Text = "–", AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(6, 6, 6, 0) });
            table.Controls.Add(_to);
        }

        public void Set(BannedHours ban)
        {
            _from.SelectedIndex = Math.Clamp(ban.From, 0, 23);
            _to.SelectedIndex = Math.Clamp(ban.To, 0, 23);
            _enabled.Checked = ban.Enabled;
            Sync();
        }

        public BannedHours Get() => new() { Enabled = _enabled.Checked, From = _from.SelectedIndex, To = _to.SelectedIndex };
    }
}
