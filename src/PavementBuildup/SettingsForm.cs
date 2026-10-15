using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PavementBuildup.Core;

namespace PavementBuildup;

internal sealed class SettingsForm : Form
{
    private readonly RadioButton _dark = new() { Text = "Dark (default)", AutoSize = true };
    private readonly RadioButton _light = new() { Text = "Light", AutoSize = true };
    private readonly TextBox _name = new() { Width = Brand.Px(260), PlaceholderText = "e.g. J. Smith" };
    private readonly NumericUpDown _scale = new() { Minimum = 1, Maximum = 1000, Width = Brand.Px(90) };
    private readonly ComboBox _text = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(160) };
    private readonly CheckBox _count = new() { Text = "Count my tool use in the time saved report", AutoSize = true };
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Saved just for you in " + UserSettings.FilePath };

    private static readonly (string Label, int Size)[] Sizes = { ("Normal", 100), ("Larger", 115), ("Largest", 130) };

    public SettingsForm()
    {
        var s = UserSettings.Current;
        Text = "My settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        _dark.Checked = s.Dark;
        _light.Checked = !s.Dark;
        _name.Text = s.Name;
        _scale.Value = (decimal)Math.Clamp(s.DefaultScale, 1, 1000);
        foreach (var size in Sizes) _text.Items.Add(size.Label);
        _text.SelectedIndex = Array.FindIndex(Sizes, z => z.Size >= s.TextSize) is var i and >= 0 ? i : Sizes.Length - 1;
        _count.Checked = s.CountUsage;

        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 10, 16, 10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
            c.Margin = new Padding(3, 5, 3, 3);
            t.Controls.Add(c);
        }
        var theme = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        theme.Controls.Add(_dark);
        theme.Controls.Add(_light);
        Row("Theme", theme);
        Row("Your name", _name);
        var scaleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        scaleRow.Controls.Add(new Label { Text = "1 :", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        scaleRow.Controls.Add(_scale);
        Row("Default detail scale", scaleRow);
        Row("Text size", _text);
        t.Controls.Add(new Label());
        t.Controls.Add(_count);
        t.Controls.Add(new Label());
        t.Controls.Add(new Label
        {
            Text = "Your name fills \"Designed by\" when it's blank. Text size applies to windows opened after saving.",
            AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(360), 0),
        });

        var folder = new LinkLabel { Text = "Open my settings folder", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
        folder.LinkClicked += (_, _) => OpenFolder();
        t.Controls.Add(new Label());
        t.Controls.Add(folder);

        var save = new Button { Text = "Save", AutoSize = true, Tag = Brand.PrimaryTag, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
        save.Click += (_, _) => Save();
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        row.Controls.Add(save);
        row.Controls.Add(cancel);
        t.Controls.Add(new Label());
        t.Controls.Add(row);
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(t);
        Brand.Decorate(this, "My settings", "Personal to you on this PC", _status);
        t.BringToFront();
        ClientSize = Brand.FitClient(t, Brand.Px(520), hasStatus: true);
    }

    private void Save()
    {
        var s = UserSettings.Load();
        s.Theme = _light.Checked ? "Light" : "Dark";
        s.Name = _name.Text.Trim();
        s.DefaultScale = (double)_scale.Value;
        s.TextSize = Sizes[Math.Max(0, _text.SelectedIndex)].Size;
        s.CountUsage = _count.Checked;
        try
        {
            s.Save();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "Could not save: " + ex.Message;
        }
    }

    private static void OpenFolder()
    {
        var dir = Path.GetDirectoryName(UserSettings.FilePath)!;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }
}
