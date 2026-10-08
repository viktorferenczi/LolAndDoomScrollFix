using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Limiter.Protocol;

namespace Limiter.Tray;

/// <summary>Tálcaikon: 5 másodpercenként lekérdezi a szolgáltatást, és megmutatja a maradék kereteket és a védelem állapotát.</summary>
public sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan ChromeStartupGrace = TimeSpan.FromSeconds(60);

    private readonly NotifyIcon _notify;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _protection = Info();
    private readonly ToolStripMenuItem _lol = Info();
    private readonly ToolStripMenuItem _short = Info();
    private readonly ToolStripMenuItem _feed = Info();
    private readonly ToolStripMenuItem _next = Info();
    private readonly Dictionary<Health, Icon> _icons;

    private StatusForm? _form;
    private StatusView? _view;
    private DateTimeOffset? _chromeSeenSince;
    private DateTimeOffset? _lastActionShown;
    private bool _lastShortBlocked, _lastFeedBlocked, _lastLolBlocked;
    private Health _lastHealth = Health.Ok;
    private bool _refreshing;

    public TrayContext()
    {
        _icons = new()
        {
            [Health.Ok] = MakeIcon(Color.FromArgb(46, 160, 67)),
            [Health.Warning] = MakeIcon(Color.FromArgb(219, 154, 4)),
            [Health.Error] = MakeIcon(Color.FromArgb(207, 34, 46)),
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_protection, new ToolStripSeparator(), _lol, _short, _feed, _next, new ToolStripSeparator()]);
        menu.Items.Add("Részletek…", null, (_, _) => ShowForm());
        menu.Items.Add("Frissítés", null, async (_, _) => await RefreshAsync());

        _notify = new NotifyIcon
        {
            Icon = _icons[Health.Ok],
            Text = "LoL- és görgetéskorlátozó",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notify.DoubleClick += (_, _) => ShowForm();

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private static ToolStripMenuItem Info() => new() { Enabled = false };

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var chrome = ChromeRunningLongEnough(now);
            StatusView view;
            IpcResponse? response = null;
            try
            {
                response = await Task.Run(() => IpcClient.RequestAsync(
                    new IpcRequest { Type = IpcConstants.RequestTypes.Status, Source = IpcConstants.Sources.Tray },
                    TimeSpan.FromSeconds(3)));
                view = response.Ok ? StatusView.From(response, chrome, now) : StatusView.ServiceUnreachable(response.Error ?? "hiba");
            }
            catch (Exception ex)
            {
                view = StatusView.ServiceUnreachable(ex is TimeoutException ? "időtúllépés" : ex.Message);
            }

            Apply(view);
            if (response is { Ok: true }) Notify(response, view);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(StatusView view)
    {
        _view = view;
        _protection.Text = view.ProtectionText;
        _lol.Text = view.LolText;
        _short.Text = view.ShortVideoText;
        _feed.Text = view.FacebookFeedText;
        _next.Text = view.NextReleaseText;
        _next.Visible = view.NextReleaseText.Length > 0;
        _notify.Icon = _icons[view.Health];

        var tip = $"{view.LolText}\n{view.ShortVideoText}\n{view.FacebookFeedText}";
        _notify.Text = tip.Length > 127 ? tip[..127] : tip;

        if (view.Health != Health.Ok && view.Health != _lastHealth)
            _notify.ShowBalloonTip(8000, "Korlátozó: hiba", view.Problems.FirstOrDefault() ?? view.ProtectionText, ToolTipIcon.Warning);
        _lastHealth = view.Health;

        _form?.Update(view);
    }

    private void Notify(IpcResponse s, StatusView view)
    {
        var p = s.Protection;
        if (p?.LastLolActionAt is { } at && at != _lastActionShown)
        {
            if (_lastActionShown is not null || DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(1))
                _notify.ShowBalloonTip(6000, "LoL-korlát", p.LastLolAction ?? "", ToolTipIcon.Info);
            _lastActionShown = at;
        }

        Transition(ref _lastLolBlocked, s.Lol?.Blocked == true, "Elfogyott a LoL-keret", view.LolText);
        Transition(ref _lastShortBlocked, s.ShortVideo?.Blocked == true, "Elfogyott a rövidvideós keret", view.ShortVideoText);
        Transition(ref _lastFeedBlocked, s.FacebookFeed?.Blocked == true, "Elfogyott a Facebook-hírfolyam kerete", view.FacebookFeedText);

        void Transition(ref bool last, bool now, string title, string text)
        {
            if (now && !last) _notify.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
            last = now;
        }
    }

    /// <summary>Fut-e a Chrome ebben a Windows-munkamenetben legalább 1 perce (indulási türelmi idő után várjuk a bővítmény jelentkezését).</summary>
    private bool ChromeRunningLongEnough(DateTimeOffset now)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var running = false;
        foreach (var p in Process.GetProcessesByName("chrome"))
        {
            using (p)
            {
                try { running |= p.SessionId == session; }
                catch (InvalidOperationException) { }
            }
        }

        if (!running)
        {
            _chromeSeenSince = null;
            return false;
        }
        _chromeSeenSince ??= now;
        return now - _chromeSeenSince >= ChromeStartupGrace;
    }

    private void ShowForm()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new StatusForm();
            _form.FormClosed += (_, _) => _form = null;
        }
        if (_view is not null) _form.Update(_view);
        _form.Show();
        _form.Activate();
    }

    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 3);
            g.DrawLine(pen, 16, 8, 16, 17); // óramutatók
            g.DrawLine(pen, 16, 17, 22, 21);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notify.Visible = false;
            _notify.Dispose();
            _form?.Dispose();
            foreach (var icon in _icons.Values) icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
