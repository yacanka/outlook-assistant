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
        CheckIconResources();
        if (args.Length == 1 && args[0] == "--settings-only")
        {
            Console.WriteLine("Settings selection and embedded icon checks passed.");
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
            Check(provider.GetItemText(provider.Items[0]) == "PLUS AI"
                && provider.GetItemText(provider.Items[1]) == "TAB 3.2", "Provider choices use their visible service names.");
            provider.SelectedIndex = 0;
            token.Text = "synthetic-ui-fixture";
            Check(token.UseSystemPasswordChar, "The token remains masked.");
            provider.SelectedIndex = 1;
            Check(!token.Enabled && !clearToken.Enabled && !certificateMode.Enabled,
                "TAB 3.2 must not allow editing or clearing PLUS AI credentials and trust settings.");
            provider.SelectedIndex = 0;
            Check(token.Enabled && clearToken.Enabled && certificateMode.Enabled
                && token.Text == "synthetic-ui-fixture", "Switching services preserves PLUS AI credentials.");
            Check(settings.AcceptButton != null && settings.CancelButton != null,
                "Settings support keyboard save and cancel.");
            var tabs = (TabControl)FindControl(settings, "SettingsTabs");
            settings.Show();
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

    private static TextBox FindPreview(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            var preview = control as TextBox;
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
