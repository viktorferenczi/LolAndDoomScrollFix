using System.Drawing;

namespace Limiter.Tray;

/// <summary>Részletes állapotablak.</summary>
public sealed class StatusForm : Form
{
    private readonly Label _protection = Line(bold: true);
    private readonly Label _lol = Line();
    private readonly Label _short = Line();
    private readonly Label _feed = Line();
    private readonly Label _next = Line();
    private readonly Label _problems = Line();
    private readonly Label _schedule = Line();

    public StatusForm()
    {
        Text = "LoL- és görgetéskorlátozó";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(16);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        _schedule.ForeColor = SystemColors.GrayText;
        var note = Line();
        note.ForeColor = SystemColors.GrayText;
        note.Text = "TFT, botmeccs, gyakorlás, egyéni játék és néző mód nem fogyasztja a LoL-keretet.";
        layout.Controls.AddRange([_protection, _lol, _short, _feed, _next, _problems, _schedule, note]);
        Controls.Add(layout);
    }

    private static Label Line(bool bold = false) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(520, 0),
        Margin = new Padding(0, 4, 0, 4),
        Font = bold ? new Font("Segoe UI", 10f, FontStyle.Bold) : null,
    };

    public void Update(StatusView view)
    {
        _protection.Text = view.ProtectionText;
        _protection.ForeColor = view.Health switch
        {
            Health.Ok => Color.FromArgb(26, 127, 55),
            Health.Warning => Color.FromArgb(154, 103, 0),
            _ => Color.FromArgb(207, 34, 46),
        };
        _lol.Text = view.LolText;
        _short.Text = view.ShortVideoText;
        _feed.Text = view.FacebookFeedText;
        _next.Text = view.NextReleaseText;
        _problems.Text = string.Join("\n", view.Problems.Select(p => "• " + p));
        _problems.Visible = view.Problems.Count > 0;
        _schedule.Text = view.ScheduleText;
        _schedule.Visible = view.ScheduleText.Length > 0;
    }
}
