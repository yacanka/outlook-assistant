using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ask.ai;

internal static class PopupLayoutTests
{
    [STAThread]
    private static void Main(string[] args)
    {
        CheckSettingsSelection();
        CheckMarkdownSettings();
        CheckMemorySettings();
        CheckIconResources();
        if (args.Length == 1 && args[0] == "--settings-only")
        {
            Console.WriteLine("Settings checks passed (selection, Markdown, encrypted memory, length limits, corrupt records, embedded icon).");
            return;
        }
        Application.EnableVisualStyles();
        CheckSettingsForm();
        using (var input = new MailInputForm(false, true, "Örnek konu"))
        {
            CheckSizes(input);
            input.Scale(new SizeF(1.5f, 1.5f));
            CheckSizes(input);
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Mail yanıtı hazırlanıyor",
            "Onayınızla Outlook’un tümünü yanıtla taslağı açılacak. Göndermeden önce alıcıları ve metni kontrol edin."))
        {
            CheckSizes(progress);
            Control.CheckForIllegalCrossThreadCalls = true;
            Task.Run(() => progress.ReportChunk("İlk parça")).GetAwaiter().GetResult();
            var preview = FindPreview(progress);
            Check(preview != null && preview.ReadOnly, "Generated text must have a read-only preview.");
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (preview.Text.Length == 0 && DateTime.UtcNow < deadline) Application.DoEvents();
            Check(preview.Text == "İlk parça", "Chunks become visible while generation is still running.");
            Check(!FindButton(progress, "&Mail’e aktar").Enabled, "Preview does not approve partial text.");
            Task.Run(() => progress.ReportChunk(" ve son parça")).GetAwaiter().GetResult();
            progress.SetReady();
            Check(preview.Text == "İlk parça ve son parça", "Ready flushes the last chunk without losing or duplicating text.");
            CheckContent(progress);
            progress.Scale(new SizeF(1.5f, 1.5f));
            CheckSizes(progress);
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test"))
        {
            progress.Show();
            progress.ReportChunk("First\n\nSecond\rThird");
            progress.SetReady();
            var preview = FindPreview(progress);
            Check(preview.Lines.Length == 4 && preview.Lines[1] == "" && preview.Lines[3] == "Third",
                "Plain preview displays LF, empty lines and standalone CR on separate lines.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true))
        {
            progress.Show();
            progress.ReportChunk("# Başlık\n\n**Kal");
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var preview = FindPreview(progress);
            while (preview.Text.Length == 0 && DateTime.UtcNow < deadline) Application.DoEvents();
            Check(preview.Text.Contains("Başlık"), "Partial Markdown is visible while streaming.");
            progress.ReportChunk("ın**\nAlt satır");
            progress.SetReady("# Başlık\n\n**Kalın**\nAlt satır");
            Check(preview.Text.Contains("Kalın\nAlt satır"), "Rich preview preserves soft line breaks.");
            preview.Select(preview.Text.IndexOf("Kalın"), 5);
            Check(preview.SelectionFont.Bold, "Markdown emphasis is visibly bold in RichEdit.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true, false))
        {
            progress.Show();
            progress.SetReady("**Kalın**\nAlt satır");
            Check(FindPreview(progress).Text.Replace("\r\n", "\n") == "Kalın\nAlt satır", "Plain target preview matches its readable transfer.");
            progress.FinishAndClose();
        }
        using (var input = new MailInputForm(true, false, ""))
        {
            input.Show();
            FindButton(input, "&Hazırla").PerformClick();
            Check(input.DialogResult != DialogResult.OK, "Empty subject must still require validation.");
            CheckSizes(input);
        }
        CheckSettingsColumns();
        Console.WriteLine("All popup layout checks passed.");
    }

    private static void CheckSettingsSelection()
    {
        var settings = new Askai.AiSettings
        {
            CentralServerCertificate = "server-fixture",
            CentralCaCertificates = "ca-fixture",
            CentralBundleCertificates = "bundle-fixture"
        };
        Check(settings.CentralCertificateTrustMode == Askai.CertificateTrustMode.ServerCertificate,
            "Existing defaults must not silently select the no-revocation mode.");
        Check(settings.ActiveCentralCertificate == "server-fixture", "Server mode selects its own data.");
        settings.CentralCertificateTrustMode = Askai.CertificateTrustMode.CertificateBundle;
        Check(settings.ActiveCentralCertificate == "bundle-fixture", "Bundle mode selects its own data.");
        settings.CentralCertificateTrustMode = Askai.CertificateTrustMode.CertificateAuthority;
        Check(settings.ActiveCentralCertificate == "ca-fixture", "CA mode selects its own data.");
        settings.CentralCertificateTrustMode = Askai.CertificateTrustMode.CertificateBundle;
        Check(settings.ActiveCentralCertificate == "bundle-fixture", "Switching modes preserves the bundle.");
        settings.CentralBundleCertificates = "";
        Check(settings.ActiveCentralCertificate == "" && settings.CentralServerCertificate == "server-fixture"
            && settings.CentralCaCertificates == "ca-fixture", "Clearing the bundle leaves other trust modes intact.");
    }

    private static void CheckMarkdownSettings()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "askai-settings-test-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(folder, "settings.xml");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider><Model>fixture</Model></AiSettings>");
            var settings = Askai.AiSettings.Load(path);
            Check(!settings.ApplyMarkdown, "Existing settings default to plain text.");
            settings.ApplyMarkdown = true;
            settings.Save(path);
            Check(Askai.AiSettings.Load(path).ApplyMarkdown, "Markdown choice survives save and reload.");
            settings.ApplyMarkdown = false;
            settings.Save(path);
            Check(!Askai.AiSettings.Load(path).ApplyMarkdown, "Disabling Markdown persists.");
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }

    private static void CheckMemorySettings()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "askai-memory-test-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(folder, "settings.xml");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider></AiSettings>");
            var settings = Askai.AiSettings.Load(path);
            Check(settings.MemoryEnabled && settings.MemoryText == "", "Old settings have no active personal context.");
            string memory = "Sentetik rol: koordinatör.\r\nKısa yaz. <>&\"";
            settings.MemoryText = memory;
            settings.Save(path);
            Check(!System.IO.File.ReadAllText(path).Contains("koordinatör"), "Personal context must not be stored in plain text.");
            var loaded = Askai.AiSettings.Load(path);
            Check(loaded.MemoryText == memory && loaded.MemoryEnabled, "Multiline Unicode memory survives encryption and reload.");
            loaded.MemoryEnabled = false;
            loaded.Save(path);
            loaded = Askai.AiSettings.Load(path);
            Check(!loaded.MemoryEnabled && loaded.MemoryText == memory, "Disabling memory preserves personal context.");
            loaded.MemoryText = new string('x', 4000);
            loaded.Save(path);
            Check(Askai.AiSettings.Load(path).MemoryText.Length == 4000, "Maximum length memory is preserved.");
            loaded.MemoryText += "x";
            bool rejected = false;
            try { loaded.Save(path); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && Askai.AiSettings.Load(path).MemoryText.Length == 4000, "Oversized memory is rejected without overwriting settings.");
            loaded.MemoryText = "";
            loaded.Save(path);
            Check(Askai.AiSettings.Load(path).MemoryText == "", "Clearing memory persists.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider><MemoryText>invalid-base64</MemoryText></AiSettings>");
            bool unreadable = false;
            try { Askai.AiSettings.Load(path); }
            catch (InvalidOperationException ex) { unreadable = !ex.Message.Contains("invalid-base64"); }
            Check(unreadable, "Corrupt encrypted memory returns a safe settings error.");
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }

    private static void CheckIconResources()
    {
        Check(AssistantIcon.WindowIcon.Width == 32 && AssistantIcon.WindowIcon.Height == 32,
            "The embedded icon must load at window size without a GUI.");
        Check(AssistantIcon.RibbonImage.Width == 16 && AssistantIcon.RibbonImage.Height == 16,
            "The same resource must provide a small ribbon image.");
    }

    private static void CheckSettingsForm()
    {
        using (var settings = new Askai.AiSettingsForm())
        {
            var provider = (ComboBox)FindControl(settings, "ServiceProvider");
            var token = (TextBox)FindControl(settings, "AccessToken");
            var clearToken = FindControl(settings, "ClearToken");
            var certificateMode = FindControl(settings, "CertificateMode");
            var markdown = (CheckBox)FindControl(settings, "ApplyMarkdown");
            markdown.Checked = true;
            var memory = (TextBox)FindControl(settings, "MemoryText");
            var memoryEnabled = (CheckBox)FindControl(settings, "MemoryEnabled");
            var memoryCount = (Label)FindControl(settings, "MemoryCount");
            Check(memory.Multiline && memory.AcceptsReturn && memory.MaxLength == 0,
                "Memory supports multiline editing without silently truncating pasted text.");
            memory.Text = "Sentetik rol\r\nKısa yaz";
            Check(memoryCount.Text.StartsWith(memory.TextLength.ToString()), "Memory counter follows edits.");
            memoryEnabled.Checked = false;
            Check(memory.Text.Contains("Sentetik rol"), "Disabling memory preserves the editor text.");
            memory.Text = new string('x', 4001);
            Check(memory.TextLength == 4001, "Oversized pasted memory is retained for correction.");
            memory.Clear();
            Check(provider.GetItemText(provider.Items[0]) == "PLUS AI"
                && provider.GetItemText(provider.Items[1]) == "TAB 3.2", "Provider choices use their visible service names.");
            provider.SelectedIndex = 0;
            token.Text = "synthetic-ui-fixture";
            Check(token.UseSystemPasswordChar, "The token remains masked.");
            provider.SelectedIndex = 1;
            Check(markdown.Enabled && markdown.Checked, "Markdown preference applies independently of the provider.");
            Check(!token.Enabled && !clearToken.Enabled && !certificateMode.Enabled,
                "TAB 3.2 must not allow editing or clearing PLUS AI credentials and trust settings.");
            provider.SelectedIndex = 0;
            Check(token.Enabled && clearToken.Enabled && certificateMode.Enabled
                && token.Text == "synthetic-ui-fixture", "Switching services preserves PLUS AI credentials.");
            Check(settings.AcceptButton != null && settings.CancelButton != null,
                "Settings support keyboard save and cancel.");
            var tabs = (TabControl)FindControl(settings, "SettingsTabs");
            settings.Show();
            tabs.SelectedTab = (TabPage)memory.Parent.Parent;
            memory.Text = "Synthetic context";
            ((Button)FindControl(settings, "ClearMemory")).PerformClick();
            Check(memory.Text == "", "Clear memory empties the editor.");
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                CheckSizes(settings);
                var save = (Control)settings.AcceptButton;
                var cancel = (Control)settings.CancelButton;
                Check(settings.ClientRectangle.Contains(settings.RectangleToClient(save.RectangleToScreen(save.ClientRectangle)))
                    && settings.ClientRectangle.Contains(settings.RectangleToClient(cancel.RectangleToScreen(cancel.ClientRectangle))),
                    "Settings actions stay outside the scrolling content.");
            }
            settings.Scale(new SizeF(1.5f, 1.5f));
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                CheckSizes(settings);
            }
            settings.Close();
        }
    }

    private static Control FindControl(Control parent, string name)
    {
        var matches = parent.Controls.Find(name, true);
        Check(matches.Length == 1, "Expected exactly one control: " + name);
        return matches[0];
    }

    private static void CheckSettingsColumns()
    {
        using (var form = new Form { ClientSize = new Size(560, 640) })
        {
            var layout = new ResponsiveTableLayoutPanel { ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            for (int row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "PLUS AI sertifika modu", AutoSize = true }, 0, 0);
            layout.Controls.Add(new ComboBox { Dock = DockStyle.Fill }, 1, 0);
            var help = new Label { Text = new string('W', 300), AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(help, 1, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            buttons.Controls.Add(new Button { Text = "Sertifika seç…", AutoSize = true });
            buttons.Controls.Add(new Button { Text = "Temizle", AutoSize = true });
            layout.Controls.Add(buttons, 1, 2);
            form.Controls.Add(layout);
            CheckSizes(form);
            help.Text = "Kısa açıklama";
            CheckContent(form);
            help.Text = new string('W', 300);
            CheckContent(form);
        }
    }

    private static void CheckSizes(Form form)
    {
        form.Show();
        var initial = form.ClientSize;
        foreach (var size in new[] { initial, new Size(800, 600), new Size(440, 240), initial })
        {
            form.ClientSize = size;
            CheckContent(form);
        }
    }

    private static void CheckContent(Control parent)
    {
        parent.PerformLayout();
        Application.DoEvents();
        foreach (Control control in parent.Controls)
        {
            if (!control.Visible) continue;
            var label = control as Label;
            if (label != null && label.Text.Length > 0)
            {
                var preferred = label.GetPreferredSize(new Size(label.Width, 0));
                Check(label.Height >= preferred.Height, "Text clipped vertically: " + label.Text);
            }
            var table = parent as TableLayoutPanel;
            if (table != null)
            {
                var position = table.GetPositionFromControl(control);
                int available = table.GetColumnWidths()[position.Column];
                Check(control.Width + control.Margin.Horizontal <= available,
                    "Control overflows its column: " + control.GetType().Name);
                Check(control.Bottom + control.Margin.Bottom <= table.DisplayRectangle.Bottom,
                    "Content must be reachable through vertical scrolling.");
            }
            CheckContent(control);
        }
        var scrollable = parent as ScrollableControl;
        if (scrollable != null)
            Check(!scrollable.HorizontalScroll.Visible, "Popup must not require horizontal scrolling.");
    }

    private static RichTextBox FindPreview(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            var preview = control as RichTextBox;
            if (preview != null && preview.Multiline) return preview;
            var found = FindPreview(control);
            if (found != null) return found;
        }
        return null;
    }

    private static Button FindButton(Control parent, string text)
    {
        foreach (Control control in parent.Controls)
        {
            var button = control as Button;
            if (button != null && button.Text == text) return button;
            var found = FindButton(control, text);
            if (found != null) return found;
        }
        return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
