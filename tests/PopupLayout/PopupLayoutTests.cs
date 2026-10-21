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
        BindingRedirectTests.Run();
        if (args.Length == 1 && args[0] == "--bindings-only") return;
        CheckSettingsSelection();
        CheckMarkdownSettings();
        CheckMemorySettings();
        CheckSkillSettings();
        CheckRibbonPresetSettings();
        CheckProviderCertificates();
        CheckCertificateRouting();
        CheckIconResources();
        RibbonLayoutTests.Run();
        if (args.Length == 1 && args[0] == "--settings-only")
        {
            Console.WriteLine("Settings checks passed (selection, provider certificate persistence/routing, presets, Markdown, encrypted memory and skills, limits, corrupt records, embedded icon).");
            return;
        }
        Application.EnableVisualStyles();
        CheckRevisionPreview();
        CheckSkillInput();
        CheckBackNavigation();
        CheckPromptReopening();
        CheckModelChoices();
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
            Ready(progress);
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
            Ready(progress);
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
            Ready(progress, "# Başlık\n\n**Kalın**\nAlt satır");
            Check(preview.Text.Contains("Kalın\nAlt satır"), "Rich preview preserves soft line breaks.");
            preview.Select(preview.Text.IndexOf("Kalın"), 5);
            Check(preview.SelectionFont.Bold, "Markdown emphasis is visibly bold in RichEdit.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true, false))
        {
            progress.Show();
            Ready(progress, "**Kalın**\nAlt satır");
            Check(FindPreview(progress).Text.Replace("\r\n", "\n") == "Kalın\nAlt satır", "Plain target preview matches its readable transfer.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true))
        {
            progress.Show();
            string source = "**Başlangıç** " + new string('x', 13000);
            progress.ReportChunk(source);
            PumpUntil(() => FindPreview(progress).Text.Length > 12000);
            Check(FindPreview(progress).Text.StartsWith("**Başlangıç**"), "Large live previews stay plain until final formatting.");
            Ready(progress, source);
            Check(FindPreview(progress).Text.StartsWith("Başlangıç"), "Completion still applies Markdown to large live plain previews.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true))
        {
            progress.Show();
            progress.ClientSize = new Size(450, 500);
            Ready(progress, "| A | B |\n| --- | --- |\n| x | y |");
            Check(progress.PreparedContent.Rtf.Contains("\\cellx4320") && progress.PreparedContent.Rtf.Contains("\\cellx8640"),
                "The Outlook table keeps its normal width independently of the preview window.");
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", true))
        {
            progress.Show();
            Task ready = progress.SetReadyAsync(new string('x', 64000));
            progress.Close();
            PumpUntil(() => ready.IsCompleted);
            try { ready.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Check(!progress.WaitForApprovalAsync().GetAwaiter().GetResult(), "Closing during final rendering cannot approve a response.");
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

    private static void Ready(ProgressForm progress, string result = null)
    {
        Task task = progress.SetReadyAsync(result);
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void CheckBackNavigation()
    {
        foreach (string choice in new[] { "&Geri", "&Vazgeç", "&Mail’e aktar", "Close" })
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Test", "Test", false, true, allowBack: true))
        {
            progress.Show();
            var back = FindButton(progress, "&Geri");
            Check(back != null && back.Visible && !back.Enabled, "Back is visible but unavailable while generating.");
            back.PerformClick();
            Check(!progress.WaitForApprovalAsync().IsCompleted && !progress.BackRequested,
                "Back cannot interrupt generation or resolve approval before the preview is ready.");
            Ready(progress, "Prepared response");
            Check(back.Enabled && progress.AcceptButton == null, "Back becomes available without making Enter approve the response.");
            CheckSizes(progress);
            progress.Scale(new SizeF(1.5f, 1.5f));
            CheckSizes(progress);
            if (choice == "Close") progress.Close();
            else FindButton(progress, choice).PerformClick();
            Check(progress.BackRequested == (choice == "&Geri"), "Only Back requests a return to the prompt.");
            Check(progress.WaitForApprovalAsync().GetAwaiter().GetResult() == (choice == "&Mail’e aktar"),
                "Back, cancel and closing cannot approve content.");
            if (choice == "&Mail’e aktar")
            {
                Check(!back.Enabled, "Back cannot change the decision once applying starts.");
                back.PerformClick();
                Check(!progress.BackRequested && !cancellation.IsCancellationRequested,
                    "An approved transfer cannot be changed into navigation.");
            }
            progress.FinishAndClose();
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Improve", "Test"))
        {
            progress.Show();
            Ready(progress, "Improved selection");
            Check(!FindButton(progress, "&Geri").Visible, "Flows without a prompt screen do not offer Back.");
            progress.FinishAndClose();
        }
    }

    private static void CheckPromptReopening()
    {
        foreach (bool composing in new[] { true, false })
        using (var input = new MailInputForm(composing, !composing, "Original subject"))
        {
            var fields = input.Controls[0].Controls;
            TextBox subject = null;
            TextBox instructions = null;
            foreach (Control control in fields)
            {
                var text = control as TextBox;
                if (text == null) continue;
                if (text.Multiline) instructions = text;
                else subject = text;
            }
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                int visit = 0;
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    if (visit == 0)
                    {
                        if (composing) subject.Text = "Updated subject";
                        instructions.Text = "  First prompt\r\nSecond line  ";
                    }
                    else
                    {
                        Check(subject.Text == (composing ? "Updated subject" : "Original subject"), "Returning preserves the subject.");
                        Check(instructions.Text == "  First prompt\r\nSecond line  ", "Returning preserves the exact prompt, including whitespace.");
                        Check(subject.ReadOnly == !composing, "Returning does not make a reply subject editable.");
                        instructions.Text = "Revised prompt";
                    }
                    visit++;
                    FindButton(input, "&Hazırla").PerformClick();
                };
                timer.Start();
                Check(input.ShowDialog() == DialogResult.OK, "The first prompt can be submitted.");
                timer.Start();
                Check(input.ShowDialog() == DialogResult.OK && input.Instructions == "Revised prompt",
                    "The same input window can submit a revised prompt after going back.");
            }
        }
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!complete() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        Check(complete(), "The UI operation completes before the test deadline.");
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

    private static void CheckRevisionPreview()
    {
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Preview", "Approval"))
        {
            var completion = new TaskCompletionSource<string>();
            progress.ConfigureRevisions((text, instruction, token) => completion.Task);
            progress.Show(); Ready(progress, "Original");
            var revision = progress.ReviseAsync("Shorter");
            Check(!FindButton(progress, "&Mail’e aktar").Enabled, "Cannot transfer while revision is in flight.");
            Check(FindPreview(progress).Text == "Original", "Previous successful text stays visible during revision.");
            completion.SetResult("Revised"); PumpUntil(() => revision.IsCompleted); revision.GetAwaiter().GetResult();
            Check(progress.SelectedResult == "Revised" && FindPreview(progress).Text == "Revised", "Completed revision becomes selected output.");
            var undo = progress.RestorePreviousAsync(); PumpUntil(() => undo.IsCompleted); undo.GetAwaiter().GetResult();
            Check(progress.SelectedResult == "Original" && FindPreview(progress).Text == "Original", "Undo restores preview and transfer text together.");
            completion = new TaskCompletionSource<string>();
            revision = progress.ReviseAsync("Try again");
            completion.SetException(new InvalidOperationException("sensitive transport details"));
            PumpUntil(() => revision.IsCompleted); revision.GetAwaiter().GetResult();
            Check(progress.SelectedResult == "Original" && FindButton(progress, "&Mail’e aktar").Enabled,
                "Failed revision preserves a transferable successful version.");
            completion = new TaskCompletionSource<string>();
            revision = progress.ReviseAsync("Late"); progress.Close(); completion.SetResult("Late result");
            PumpUntil(() => revision.IsCompleted); revision.GetAwaiter().GetResult();
            Check(progress.SelectedResult == "Original" && !progress.WaitForApprovalAsync().GetAwaiter().GetResult(),
                "Closing discards late revision and never approves transfer.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var summary = new ProgressForm(cancellation, "Summary", "Read only"))
        {
            summary.ConfigureSummary(); summary.Show(); Ready(summary, "Summary text");
            Check(!FindButton(summary, "&Mail’e aktar").Visible && !FindButton(summary, "&Mail’e aktar").Enabled,
                "Summary offers no transfer action.");
            summary.Close();
            Check(!summary.WaitForApprovalAsync().GetAwaiter().GetResult(), "Closing summary cannot write a mail.");
        }
    }

    private static void CheckSkillInput()
    {
        using (var input = new MailInputForm(true, false, "Topic", Askai.AiSkillCatalog.Defaults()))
        {
            input.Show();
            var selector = (ComboBox)FindControl(input, "SkillSelector");
            selector.SelectedIndex = 1;
            FindButton(input, "Talimatı ekle").PerformClick();
            Check(input.Instructions.Contains("nazikçe reddet"), "Selected skill becomes editable mail instructions.");
            CheckSizes(input);
        }
        using (var host = new Form())
        using (var editor = new SkillLibraryControl())
        {
            host.Controls.Add(editor);
            host.Show();
            editor.Skills = Askai.AiSkillCatalog.Defaults();
            var list = (ListBox)FindControl(editor, "SkillList");
            list.SelectedIndex = 0; FindButton(editor, "Sil").PerformClick();
            Check(editor.Skills.Count == 3, "Removing a skill changes the settings draft.");
        }
    }

    private static void CheckSkillSettings()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "askai-skills-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(folder, "settings.xml");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider></AiSettings>");
            var settings = Askai.AiSettings.Load(path);
            Check(settings.Skills.Count == 4, "Existing settings receive starter skills.");
            settings.Skills.Clear();
            settings.Skills.Add(new Askai.AiSkill("Sentetik beceri", "Sentetik talimat <>&"));
            settings.Save(path);
            Check(!System.IO.File.ReadAllText(path).Contains("Sentetik"), "Skill names and instructions are encrypted.");
            Check(Askai.AiSettings.Load(path).Skills[0].Instructions == "Sentetik talimat <>&", "Skills survive encrypted reload.");
            settings.Skills.Clear(); settings.Save(path);
            Check(Askai.AiSettings.Load(path).Skills.Count == 0, "Deleted starters are not recreated.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider><Skills>broken</Skills></AiSettings>");
            bool rejected = false;
            try { Askai.AiSettings.Load(path); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Corrupt library is not silently replaced with defaults.");
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

    private static void CheckProviderCertificates()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "askai-certificates-test-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(folder, "settings.xml");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Legacy</Provider>" +
                "<CentralServerCertificate>central-server</CentralServerCertificate><CentralCertificateTrustMode>CertificateAuthority</CentralCertificateTrustMode>" +
                "<LegacyServerCertificate>legacy-server</LegacyServerCertificate><LegacyCaCertificates>legacy-ca</LegacyCaCertificates>" +
                "<LegacyBundleCertificates>legacy-bundle</LegacyBundleCertificates><LegacyCertificateTrustMode>CertificateBundle</LegacyCertificateTrustMode></AiSettings>");
            Askai.AiSettings.Load(path).Save(path);
            var saved = System.Xml.Linq.XElement.Load(path);
            Check((string)saved.Element("LegacyBundleCertificates") == "legacy-bundle"
                && (string)saved.Element("LegacyCaCertificates") == "legacy-ca"
                && (string)saved.Element("LegacyServerCertificate") == "legacy-server"
                && (string)saved.Element("LegacyCertificateTrustMode") == "CertificateBundle",
                "Each legacy certificate mode survives reload and save.");
            Check((string)saved.Element("CentralServerCertificate") == "central-server"
                && (string)saved.Element("CentralCertificateTrustMode") == "CertificateAuthority",
                "Saving legacy certificates preserves the original PLUS AI certificate keys.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Legacy</Provider></AiSettings>");
            Askai.AiSettings.Load(path).Save(path);
            saved = System.Xml.Linq.XElement.Load(path);
            Check((string)saved.Element("LegacyServerCertificate") == ""
                && (string)saved.Element("LegacyCertificateTrustMode") == "ServerCertificate",
                "Old legacy settings use system trust and retain the secure certificate mode default.");
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }

    private static void CheckRibbonPresetSettings()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "askai-preset-test-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(folder, "settings.xml");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider></AiSettings>");
            Askai.AiSettings.Load(path).Save(path);
            Check((string)System.Xml.Linq.XElement.Load(path).Element("RibbonPreset") == "Standard",
                "Old settings use the original ribbon layout.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Legacy</Provider><RibbonPreset>RepliesMenu</RibbonPreset></AiSettings>");
            Askai.AiSettings.Load(path).Save(path);
            Check((string)System.Xml.Linq.XElement.Load(path).Element("RibbonPreset") == "RepliesMenu",
                "The selected ribbon layout survives reload and save independently of the provider.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider><RibbonPreset>999</RibbonPreset></AiSettings>");
            Askai.AiSettings.Load(path).Save(path);
            Check((string)System.Xml.Linq.XElement.Load(path).Element("RibbonPreset") == "Standard",
                "An unknown preset safely falls back to the original layout.");
            System.IO.File.WriteAllText(path, "<AiSettings><Provider>Central</Provider><RibbonPreset>RepliesMenu</RibbonPreset>"
                + "<Token>invalid-base64</Token><MemoryText>invalid-base64</MemoryText></AiSettings>");
            Check(Askai.AiSettings.LoadRibbonPreset(path) == Askai.AiRibbonPreset.RepliesMenu,
                "Ribbon startup reads its preference without decrypting credentials or memory.");
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }

    private static void CheckCertificateRouting()
    {
        string centralUrl = Askai.AppConfig.CentralApiUrl;
        string legacyUrl = Askai.AppConfig.ApiUrl;
        // An accidentally bypassed certificate must still be unable to reach a network endpoint.
        Askai.AppConfig.CentralApiUrl = "invalid";
        Askai.AppConfig.ApiUrl = "invalid";
        try
        {
            foreach (Askai.AiProvider provider in Enum.GetValues(typeof(Askai.AiProvider)))
            foreach (Askai.CertificateTrustMode mode in Enum.GetValues(typeof(Askai.CertificateTrustMode)))
            {
                var settings = new Askai.AiSettings { Provider = provider,
                    CentralCertificateTrustMode = mode, LegacyCertificateTrustMode = mode };
                if (provider == Askai.AiProvider.Central)
                {
                    if (mode == Askai.CertificateTrustMode.ServerCertificate) settings.CentralServerCertificate = "invalid";
                    else if (mode == Askai.CertificateTrustMode.CertificateAuthority) settings.CentralCaCertificates = "invalid";
                    else settings.CentralBundleCertificates = "invalid";
                }
                else
                {
                    if (mode == Askai.CertificateTrustMode.ServerCertificate) settings.LegacyServerCertificate = "invalid";
                    else if (mode == Askai.CertificateTrustMode.CertificateAuthority) settings.LegacyCaCertificates = "invalid";
                    else settings.LegacyBundleCertificates = "invalid";
                }
                bool rejected = false;
                try
                {
                    var task = new Askai.AiService().SendStreamingRequestAsync("synthetic", null, CancellationToken.None, settings);
                    task.GetAwaiter().GetResult();
                }
                catch (Askai.AiServiceException) { rejected = true; }
                Check(rejected, "The selected " + provider + " " + mode + " certificate is validated before transport starts.");
            }
        }
        finally { Askai.AppConfig.CentralApiUrl = centralUrl; Askai.AppConfig.ApiUrl = legacyUrl; }
    }

    private static void CheckSettingsForm()
    {
        using (var settings = new Askai.AiSettingsForm())
        {
            settings.Show();
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
            Check(!token.Visible && !clearToken.Visible && !FindControl(settings, "PlusAiAccess").Visible,
                "TAB 3.2 hides the complete PLUS AI credentials section.");
            Check(certificateMode.Enabled, "Both services allow their own certificate settings.");
            provider.SelectedIndex = 0;
            Check(token.Enabled && clearToken.Enabled && certificateMode.Enabled
                && token.Text == "synthetic-ui-fixture", "Switching services preserves PLUS AI credentials.");
            Check(settings.AcceptButton != null && settings.CancelButton != null,
                "Settings support keyboard save and cancel.");
            var tabs = (TabControl)FindControl(settings, "SettingsTabs");
            Check(tabs.TabPages[1].Text == "Gelişmiş", "Certificate settings use the Advanced tab.");
            tabs.SelectedIndex = 1;
            var modes = (ComboBox)certificateMode;
            var setCertificate = typeof(Askai.AiSettingsForm).GetMethod("SetCurrentCertificateData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var currentCertificate = typeof(Askai.AiSettingsForm).GetProperty("CurrentCertificateData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            modes.SelectedIndex = 0;
            setCertificate.Invoke(settings, new object[] { "synthetic-central-certificate" });
            provider.SelectedIndex = 1;
            modes.SelectedIndex = 1;
            setCertificate.Invoke(settings, new object[] { "synthetic-legacy-ca" });
            provider.SelectedIndex = 0;
            Check(modes.SelectedIndex == 0 && (string)currentCertificate.GetValue(settings) == "synthetic-central-certificate",
                "Switching back restores the PLUS AI mode and certificate draft.");
            provider.SelectedIndex = 1;
            Check(modes.SelectedIndex == 1 && (string)currentCertificate.GetValue(settings) == "synthetic-legacy-ca",
                "Switching back restores the TAB 3.2 mode and certificate draft.");
            ((Button)FindControl(settings, "ClearCertificate")).PerformClick();
            Check((string)currentCertificate.GetValue(settings) == "", "Clear empties only the active service and mode.");
            provider.SelectedIndex = 0;
            Check((string)currentCertificate.GetValue(settings) == "synthetic-central-certificate", "Clearing TAB trust preserves PLUS AI trust.");
            var preset = (ComboBox)FindControl(settings, "RibbonPreset");
            var appearancePage = (TabPage)markdown.Parent.Parent;
            Check(appearancePage.Text == "Görünüm" && preset.Parent == markdown.Parent,
                "Appearance settings group the ribbon and Markdown preferences on the renamed tab.");
            Check((string)preset.Items[0] == "Ayrı yanıt düğmeleri"
                && (string)preset.Items[1] == "Yanıt menüsü, büyük düğmeler",
                "Preset labels describe the layouts without numbers and preserve their order.");
            var appearanceLayout = (TableLayoutPanel)markdown.Parent;
            var markdownHeading = (Label)appearanceLayout.GetControlFromPosition(0, appearanceLayout.GetRow(markdown) - 1);
            Check(markdownHeading.Text == "Markdown" && markdownHeading.Font.Bold && markdownHeading.Padding.Top >= 16,
                "Markdown has a distinct heading and space separating it from the preset selector.");
            var tooltip = (ToolTip)typeof(Askai.AiSettingsForm).GetField("help",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(settings);
            Check(!System.Text.RegularExpressions.Regex.IsMatch(tooltip.GetToolTip(preset), @"\d"),
                "Preset help contains no numeric references.");
            Check(tooltip.GetToolTip(markdown).Contains("Markdown işaretlerini")
                && tooltip.GetToolTip(markdown).Contains("Outlook"),
                "Markdown help explains the conversion and where it is applied.");
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

    private static void CheckModelChoices()
    {
        var originalModels = Askai.AppConfig.CentralModels;
        try
        {
            foreach (var models in new[] { new string[0], new[] { "only-model" }, new[] { "first-model", "second-model" } })
            {
                Askai.AppConfig.CentralModels = models;
                using (var settings = new Askai.AiSettingsForm())
                {
                    settings.Show();
                    ((ComboBox)FindControl(settings, "ServiceProvider")).SelectedIndex = 0;
                    var selector = (ComboBox)FindControl(settings, "Model");
                    Check(selector.Visible == (models.Length > 1), "Model choices are shown only for multiple models.");
                    Check(FindControl(settings, "ModelLabel").Visible == selector.Visible, "Hidden model choices leave no orphan label.");
                    if (models.Length == 1)
                        Check((string)selector.SelectedItem == "only-model", "The single model is selected even when its field is hidden.");
                    var tabs = (TabControl)FindControl(settings, "SettingsTabs");
                    tabs.SelectedIndex = 1;
                    var service = (ComboBox)FindControl(settings, "ServiceProvider");
                    service.SelectedIndex = 1;
                    service.SelectedIndex = 0;
                    tabs.SelectedIndex = 0;
                    Check(selector.Visible == (models.Length > 1)
                        && FindControl(settings, "ModelLabel").Visible == selector.Visible,
                        "Switching services on another tab preserves model and label visibility.");
                    settings.Close();
                }
            }
        }
        finally { Askai.AppConfig.CentralModels = originalModels; }
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
