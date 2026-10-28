using System.Drawing;
using Limiter.Core;

namespace Limiter.Tray;

/// <summary>Jelszó bekérése (egy mező) vagy új jelszó megadása (két mező, ellenőrzéssel).</summary>
public sealed class PasswordForm : Form
{
    private readonly TextBox _first = Field();
    private readonly TextBox? _second;

    public string Password => _first.Text;

    /// <param name="confirm">Igaz: új jelszó, megerősítéssel és az erősségi szabályok ellenőrzésével.</param>
    /// <param name="notice">Piros figyelmeztetés a mezők fölött (pl. hibás jelszó után).</param>
    public PasswordForm(string title, string prompt, bool confirm, string? notice = null)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(14);
        TopMost = true;

        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 8) });
        if (notice is not null)
            panel.Controls.Add(new Label { Text = notice, AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.Firebrick, Margin = new Padding(0, 0, 0, 8) });

        panel.Controls.Add(new Label { Text = confirm ? "Új jelszó:" : "Jelszó:", AutoSize = true });
        panel.Controls.Add(_first);
        if (confirm)
        {
            _second = Field();
            panel.Controls.Add(new Label { Text = "Új jelszó újra:", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            panel.Controls.Add(_second);
            panel.Controls.Add(new Label
            {
                Text = $"Legalább {AdminPassword.MinLength} karakter. Ez a jelszó kell az admin beállításokhoz és a program eltávolításához.",
                AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0),
            });
        }

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Mégse", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 0) };
        ok.Click += (_, _) => Accept(confirm);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([cancel, ok]);
        panel.Controls.Add(buttons);

        Controls.Add(panel);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { Activate(); _first.Focus(); };
    }

    private void Accept(bool confirm)
    {
        if (confirm)
        {
            var error = AdminPassword.Validate(_first.Text);
            if (error is null && _first.Text != _second!.Text) error = "A két jelszó nem egyezik.";
            if (error is not null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else if (_first.Text.Length == 0)
        {
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static TextBox Field() => new() { UseSystemPasswordChar = true, Width = 300, MaxLength = AdminPassword.MaxLength + 1 };
}
