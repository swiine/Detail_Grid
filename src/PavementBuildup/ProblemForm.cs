using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PavementBuildup.Core;

namespace PavementBuildup;

internal sealed class ProblemForm : Form
{
    private readonly TextBox _what = new()
    {
        Multiline = true, Width = Brand.Px(520), Height = Brand.Px(110), ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "What were you doing, and what went wrong? (e.g. clicked Draw joints on an L-shaped hatch, nothing appeared)",
    };
    private readonly CheckBox _shot = new() { Text = "Include a screenshot", AutoSize = true, Checked = true };
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Makes a zip on your Desktop with the error log, your settings and the screenshot. Nothing is sent automatically." };
    private readonly string _app;
    private readonly string _version;
    private readonly byte[]? _screenshot;
    private readonly IEnumerable<string> _extra;

    public ProblemForm(string app, byte[]? screenshotPng, IEnumerable<string>? extraFiles = null, string? error = null)
    {
        _app = app;
        _version = typeof(ProblemForm).Assembly.GetName().Version?.ToString(3) ?? "";
        _screenshot = screenshotPng;
        _extra = extraFiles ?? Array.Empty<string>();
        Text = "Report a problem";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        _shot.Enabled = _shot.Checked = screenshotPng is { Length: > 0 };

        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 10, 16, 10) };
        if (!string.IsNullOrWhiteSpace(error))
            t.Controls.Add(new Label { Text = "Something went wrong: " + error, AutoSize = true, ForeColor = Brand.Failure, MaximumSize = new Size(Brand.Px(520), 0), Margin = new Padding(3, 3, 3, 8) });
        t.Controls.Add(Brand.Step("What happened?"));
        t.Controls.Add(_what);
        t.Controls.Add(_shot);
        var make = new Button { Text = "Make the report", AutoSize = true, Tag = Brand.PrimaryTag, MinimumSize = new Size(Brand.Px(140), Brand.Px(34)) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
        make.Click += (_, _) => Make();
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        row.Controls.Add(make);
        row.Controls.Add(cancel);
        t.Controls.Add(row);
        CancelButton = cancel;
        Controls.Add(t);
        Brand.Decorate(this, "Report a problem", $"{app} {_version}  ·  send the zip to whoever looks after TTW Pavement", _status);
        t.BringToFront();
        ClientSize = Brand.FitClient(t, Brand.Px(560), hasStatus: true);
    }

    private void Make()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = ErrorReport.Create(Directory.Exists(desktop) ? desktop : ErrorLog.Folder, _app, _version, _what.Text,
                _shot.Checked ? _screenshot : null, ErrorReport.SettingsFiles().Concat(_extra));
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
            MessageBox.Show(this, $"Saved:\n{path}\n\nEmail or Teams it to whoever looks after TTW Pavement.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "Could not make the report: " + ex.Message;
        }
    }

    public static byte[]? Screenshot(Rectangle area)
    {
        try
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            using var bmp = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException or ArgumentException) { return null; }
    }
}
