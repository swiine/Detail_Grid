using System.Drawing;
using System.Windows.Forms;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>API key, model and on/off for "Interpret with AI".</summary>
internal sealed class AiSettingsForm : Form
{
    private readonly AiSettings _settings;
    private readonly CadStandard _standard;
    private readonly CheckBox _enabled = new() { Text = "Interpret entered text with AI (uses the AI Pavement Build-up Prompt)", AutoSize = true };
    private readonly TextBox _key = new() { Width = 420, UseSystemPasswordChar = true, PlaceholderText = "sk-ant-... (leave blank to keep the saved key)" };
    private readonly Label _keyStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _model = new() { Width = 200 };
    private readonly TextBox _test = new() { Width = 520, Text = "60mm pavers on 30 mortar on 2 layers 150 dgb20, subgrade compacted 98% mdd" };
    private readonly TextBox _result = new() { Width = 520, Height = 60, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

    public AiSettingsForm(AiSettings settings, CadStandard standard)
    {
        _settings = settings;
        _standard = standard;
        Text = "Pavement Build-up — AI settings";
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = SystemFonts.MessageBoxFont ?? Font;

        _enabled.Checked = settings.Enabled;
        _model.Text = settings.Model;
        UpdateKeyStatus();

        var remove = new Button { Text = "Remove saved key", AutoSize = true };
        remove.Click += (_, _) => { AiService.SaveKey(null); UpdateKeyStatus(); };
        var test = new Button { Text = "Test", AutoSize = true };
        test.Click += async (_, _) => await RunTest(test);
        var ok = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        AcceptButton = ok;
        CancelButton = cancel;

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10), Dock = DockStyle.Fill };
        void Row(string label, Control c) { t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }); t.Controls.Add(c); }
        Row("", _enabled);
        Row("API key:", Flow(_key, remove));
        Row("", _keyStatus);
        Row("Model:", _model);
        Row("", new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText,
            Text = "Uses the Claude API (console.anthropic.com for a key). The key is saved encrypted for your Windows login only; " +
                   "the ANTHROPIC_API_KEY environment variable also works. Your build-up text is sent to the API when you click Fill table, " +
                   "pick a note, or use PAVEQUICK/PAVETEXT. Instructions: the CAD standard's AI prompt file, or the built-in prompt.",
        });
        Row("Try it:", Flow(_test, test));
        Row("", _result);
        Row("", Flow(ok, cancel));
        Controls.Add(t);
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        f.Controls.AddRange(controls);
        return f;
    }

    private void UpdateKeyStatus()
    {
        var saved = AiService.SavedKey;
        _keyStatus.Text = saved is not null ? $"Saved key ending …{saved[^Math.Min(4, saved.Length)..]}"
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) ? "Using the ANTHROPIC_API_KEY environment variable"
            : "No key saved yet";
    }

    private void Apply()
    {
        if (!string.IsNullOrWhiteSpace(_key.Text))
        {
            AiService.SaveKey(_key.Text);
            _key.Clear();
        }
        _settings.Enabled = _enabled.Checked;
        _settings.Model = string.IsNullOrWhiteSpace(_model.Text) ? new AiSettings().Model : _model.Text.Trim();
        UpdateKeyStatus();
    }

    private void Save()
    {
        Apply();
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task RunTest(Button button)
    {
        Apply();
        button.Enabled = false;
        UseWaitCursor = true;
        _result.Text = "Asking the AI…";
        try
        {
            var probe = new AiSettings { Enabled = true, Model = _settings.Model };
            if (!AiService.IsReady(probe, out var reason))
            {
                _result.Text = reason;
                return;
            }
            var converted = await AiService.InterpretAsync(_test.Text, probe, _standard);
            var parsed = BuildupParser.ParseBuildup(converted, new List<string>());
            _result.Text = converted + Environment.NewLine + $"→ {parsed.Layers.Count} course(s) read.";
        }
        catch (InvalidOperationException ex)
        {
            _result.Text = ex.Message;
        }
        finally
        {
            button.Enabled = true;
            UseWaitCursor = false;
        }
    }
}
