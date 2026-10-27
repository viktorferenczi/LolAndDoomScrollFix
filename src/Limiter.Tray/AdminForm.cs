using System.Drawing;
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
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(14);

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };

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

        var save = new Button { Text = "Mentés", AutoSize = true, DialogResult = DialogResult.None, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Mégse", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 0) };
        save.Click += (_, _) => Save();
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.AddRange([cancel, save]);
        root.Controls.Add(buttons);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;

        _rolling.CheckedChanged += (_, _) => _resetHour.Enabled = _daily.Checked;
        _daily.CheckedChanged += (_, _) => _resetHour.Enabled = _daily.Checked;

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
    };

    private void Save()
    {
        var settings = Collect();
        var errors = SettingsFile.Validate(settings);
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
