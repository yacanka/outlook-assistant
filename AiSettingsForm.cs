using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;

namespace Askai
{
    public sealed class AiSettingsForm : Form
    {
        internal static event Action<AiRibbonPreset> RibbonPresetSaved;
        private readonly ComboBox ribbonPreset = new ComboBox
        {
            Name = "RibbonPreset", AccessibleName = "Şerit düzeni", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill
        };
        private readonly ComboBox provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly Label modelLabel = FieldLabel("&Model");
        private readonly TextBox token = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        private readonly CheckBox applyMarkdown = new CheckBox { Name = "ApplyMarkdown", Text = "Markdown uygula", AutoSize = true };
        private readonly CheckBox memoryEnabled = new CheckBox { Name = "MemoryEnabled", Text = "Hafızayı kullan", AutoSize = true };
        private readonly TextBox memoryText = new TextBox
        {
            Name = "MemoryText", AccessibleName = "Kişisel hafıza", Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 180, MaxLength = 0
        };
        private readonly Label memoryCount = new Label { Name = "MemoryCount", AutoSize = true, Dock = DockStyle.Fill };
        private readonly CheckBox tls12 = new CheckBox { Text = "TLS 1.2 uyumluluk modu", AutoSize = true };
        private readonly ComboBox certificateMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ToolTip help = new ToolTip { AutoPopDelay = 10000 };
        private readonly TextBox certificateInfo = new TextBox
        {
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 104
        };
        private readonly Button selectCertificate = new Button { Text = "Sertifika seç…", AutoSize = true };
        private readonly Button clearCertificate = new Button { Name = "ClearCertificate", Text = "Temizle", AutoSize = true };
        private readonly Button clearToken = new Button { Name = "ClearToken", Text = "Token’ı sil", AutoSize = true };
        private readonly Label certificateSection = new Label { Name = "CertificateService", AutoSize = true, Dock = DockStyle.Fill };
        private TableLayoutPanel plusAccess;
        private AiSettings draft = new AiSettings();
        private AiProvider certificateProvider;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource certificateInfoCancellation;
        private int certificateInfoVersion;
        private bool resourcesDisposed;
        private bool saving;
        private bool selectingCertificate;
        private Button saveButton;
        private TabControl settingsTabs;
        private CertificateTrustMode CurrentCertificateMode => certificateMode.SelectedIndex == 2
            ? CertificateTrustMode.CertificateBundle : certificateMode.SelectedIndex == 1
                ? CertificateTrustMode.CertificateAuthority : CertificateTrustMode.ServerCertificate;
        private string CurrentCertificateData => certificateProvider == AiProvider.Central
            ? draft.ActiveCentralCertificate : draft.ActiveLegacyCertificate;

        public AiSettingsForm()
        {
            Text = AiBrand.Name + " — Ayarlar";
            Icon = Ask.ai.AssistantIcon.WindowIcon;
            Font = new Font("Segoe UI", 10f);
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 480);
            MinimumSize = SizeFromClientSize(new Size(400, 300));
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new Ask.ai.ResponsiveTableLayoutPanel
            {
                AutoScroll = false, Padding = new Padding(12), ColumnCount = 1, RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tabs = new TabControl { Name = "SettingsTabs", Dock = DockStyle.Fill, Padding = new Point(8, 5), TabIndex = 0 };
            settingsTabs = tabs;
            var servicePage = new TabPage("Servis") { BackColor = SystemColors.Window };
            var connectionPage = new TabPage("Gelişmiş") { BackColor = SystemColors.Window };
            tabs.TabPages.Add(servicePage);
            tabs.TabPages.Add(connectionPage);
            var formattingPage = new TabPage("Görünüm") { BackColor = SystemColors.Window };
            tabs.TabPages.Add(formattingPage);
            var formattingLayout = CreatePageLayout();
            AddRow(formattingLayout, SectionLabel("&Şerit düzeni"));
            ribbonPreset.Items.AddRange(new object[] { "Ayrı yanıt düğmeleri", "Yanıt menüsü, büyük düğmeler" });
            AddRow(formattingLayout, ribbonPreset);
            help.SetToolTip(ribbonPreset, "Ayrı yanıt düğmeleri: Mail Hazırla büyük; yanıt ve ayarlar küçük.\nYanıt menüsü, büyük düğmeler: Yanıt seçenekleri tek büyük menüde; ayarlar büyük.");
            AddRow(formattingLayout, SectionLabel("Markdown"));
            AddRow(formattingLayout, applyMarkdown);
            help.SetToolTip(applyMarkdown, "AI yanıtındaki Markdown işaretlerini önizlemede ve Outlook mailinde başlık, kalın yazı, liste ve tablo biçimine dönüştürür.");
            formattingPage.Controls.Add(formattingLayout);
            layout.Controls.Add(tabs, 0, 0);
            BuildServicePage(servicePage);
            BuildConnectionPage(connectionPage);
            var memoryPage = new TabPage("Hafıza") { BackColor = SystemColors.Window };
            tabs.TabPages.Add(memoryPage);
            BuildMemoryPage(memoryPage);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0), TabIndex = 1
            };
            var save = new Button { Text = "&Kaydet", AutoSize = true, TabIndex = 0 };
            saveButton = save;
            StyleButton(save);
            save.BackColor = SystemColors.Highlight;
            save.ForeColor = SystemColors.HighlightText;
            save.FlatStyle = FlatStyle.Flat;
            save.FlatAppearance.BorderSize = 0;
            save.Click += SaveSettings;
            var cancel = new Button { Text = "&Vazgeç", AutoSize = true, DialogResult = DialogResult.Cancel, TabIndex = 1 };
            StyleButton(cancel);
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;
            provider.FormattingEnabled = true;
            provider.Format += (sender, args) => args.Value = (AiProvider)args.Value == AiProvider.Central ? AiBrand.PlusAi : AiBrand.Tab32;
            provider.Items.AddRange(new object[] { AiProvider.Central, AiProvider.Legacy });
            model.Items.AddRange(AppConfig.CentralModels);
            AiSettings settings;
            try { settings = AiSettings.Load(); }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                settings = new AiSettings();
            }
            draft = settings;
            certificateProvider = settings.Provider;
            provider.SelectedItem = settings.Provider;
            model.SelectedItem = settings.Model;
            if (model.SelectedIndex < 0 && model.Items.Count > 0) model.SelectedIndex = 0;
            token.Text = settings.Token;
            tls12.Checked = settings.UseTls12;
            applyMarkdown.Checked = settings.ApplyMarkdown;
            ribbonPreset.SelectedIndex = (int)settings.RibbonPreset;
            memoryEnabled.Checked = settings.MemoryEnabled;
            memoryText.Text = settings.MemoryText;
            UpdateMemoryCount();
            certificateMode.SelectedIndexChanged += (sender, args) =>
            {
                if (certificateProvider == AiProvider.Central) draft.CentralCertificateTrustMode = CurrentCertificateMode;
                else draft.LegacyCertificateTrustMode = CurrentCertificateMode;
                UpdateCertificateInfo();
            };
            provider.SelectedIndexChanged += (s, e) => { ChangeCertificateProvider(); UpdateEnabled(); };
            ChangeCertificateProvider();
            UpdateEnabled();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true;
                lifetime.Cancel();
                if (certificateInfoCancellation != null) { certificateInfoCancellation.Cancel(); certificateInfoCancellation.Dispose(); }
                lifetime.Dispose();
                help.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateCertificateInfo();
        }

        private void BuildMemoryPage(TabPage page)
        {
            var layout = CreatePageLayout();
            AddRow(layout, memoryEnabled);
            help.SetToolTip(memoryEnabled, "Her mail işleminde kullanılır. Kapatmak kayıtlı bilgileri silmez.");
            AddRow(layout, FieldLabel("&Kişisel bilgiler ve yazım tercihleri"));
            AddRow(layout, memoryText);
            AddRow(layout, memoryCount);
            help.SetToolTip(memoryText, "Rolünüzü ve yazım tercihlerinizi girin. Bilgiler şifreli saklanır; etkinken tamamı AI servisine gönderilir.");
            var clear = new Button { Name = "ClearMemory", Text = "Temizle", AutoSize = true };
            StyleButton(clear);
            clear.Click += (sender, args) => memoryText.Clear();
            var actions = CreateButtonRow();
            actions.Controls.Add(clear);
            AddRow(layout, actions);
            memoryText.TextChanged += (sender, args) => UpdateMemoryCount();
            page.Controls.Add(layout);
        }

        private void UpdateMemoryCount()
        {
            bool tooLong = memoryText.TextLength > AppConfig.MemoryMaxLength;
            memoryCount.Text = memoryText.TextLength + " / 4.000 karakter" +
                (tooLong ? " — Kaydetmeden önce metni kısaltın." : "");
            memoryCount.ForeColor = tooLong ? Color.Firebrick : SystemColors.WindowText;
        }

        private void BuildServicePage(TabPage page)
        {
            var layout = CreatePageLayout();
            provider.Name = "ServiceProvider";
            provider.AccessibleName = "Servis";
            model.Name = "Model";
            model.AccessibleName = "PLUS AI model";
            token.Name = "AccessToken";
            token.AccessibleName = "PLUS AI erişim token’ı";
            AddRow(layout, FieldLabel("&Servis"));
            AddRow(layout, provider);
            plusAccess = new TableLayoutPanel
            {
                Name = "PlusAiAccess", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0
            };
            plusAccess.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(plusAccess, SectionLabel("PLUS AI erişimi"));
            modelLabel.Name = "ModelLabel";
            AddRow(plusAccess, modelLabel);
            AddRow(plusAccess, model);
            AddRow(plusAccess, FieldLabel("&Erişim token’ı (gerekli)"));
            AddRow(plusAccess, token);
            StyleButton(clearToken);
            clearToken.Click += (sender, args) => token.Clear();
            var tokenActions = CreateButtonRow();
            tokenActions.Controls.Add(clearToken);
            AddRow(plusAccess, tokenActions);
            AddRow(layout, plusAccess);
            help.SetToolTip(token, "Bu Windows kullanıcısı için şifreli saklanır.");
            help.SetToolTip(clearToken, "Token’ı silmek için temizleyip Kaydet’e basın.");
            page.Controls.Add(layout);
        }

        private void BuildConnectionPage(TabPage page)
        {
            var layout = CreatePageLayout();
            AddRow(layout, tls12);
            help.SetToolTip(tls12, "TLS bağlantı hatasında deneyin. Sertifika doğrulaması açık kalır.");
            certificateSection.Font = new Font(Font, FontStyle.Bold);
            AddRow(layout, certificateSection);
            AddRow(layout, FieldLabel("&Sertifika modu"));
            certificateMode.Name = "CertificateMode";
            certificateMode.AccessibleName = "Seçili servis için sertifika modu";
            certificateMode.Items.AddRange(new object[] { "Sunucu + ara/kök zinciri", "CA zincirine güven", "Sertifika paketi (CRL/OCSP kapalı)" });
            AddRow(layout, certificateMode);
            certificateInfo.AccessibleName = "Seçilen sertifika bilgileri";
            certificateInfo.BackColor = SystemColors.Control;
            help.SetToolTip(certificateInfo, "Sertifika seçilmezse Windows sertifika doğrulaması kullanılır.");
            AddRow(layout, certificateInfo);
            var buttons = CreateButtonRow();
            StyleButton(selectCertificate);
            StyleButton(clearCertificate);
            buttons.Controls.Add(selectCertificate);
            buttons.Controls.Add(clearCertificate);
            AddRow(layout, buttons);
            selectCertificate.Click += SelectServerCertificate;
            clearCertificate.Click += (sender, args) => { SetCurrentCertificateData(""); UpdateCertificateInfo(); };
            page.Controls.Add(layout);
        }

        private static Ask.ai.ResponsiveTableLayoutPanel CreatePageLayout()
        {
            var layout = new Ask.ai.ResponsiveTableLayoutPanel { Padding = new Padding(10), ColumnCount = 1, RowCount = 0 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.TabIndex = row;
            control.Margin = new Padding(0, 2, 0, 4);
            layout.Controls.Add(control, 0, row);
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill };
        }

        private Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 16, 0, 4)
            };
        }

        private static FlowLayoutPanel CreateButtonRow()
        {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        }

        private static void StyleButton(Button button)
        {
            button.MinimumSize = new Size(112, 34);
            button.Padding = new Padding(8, 3, 8, 3);
            button.Margin = new Padding(0, 0, 8, 0);
        }

        private void UpdateEnabled()
        {
            bool central = (AiProvider)provider.SelectedItem == AiProvider.Central;
            help.SetToolTip(provider, central
                ? "PLUS AI için model ve erişim token’ı gerekir."
                : "TAB 3.2 mevcut servis bağlantısını kullanır.");
            clearToken.Enabled = central;
            plusAccess.Visible = central;
            model.Enabled = central;
            bool showModel = central && model.Items.Count > 1;
            model.Visible = showModel;
            modelLabel.Visible = showModel;
            token.Enabled = central;
        }

        private void ChangeCertificateProvider()
        {
            certificateProvider = (AiProvider)provider.SelectedItem;
            certificateSection.Text = (certificateProvider == AiProvider.Central ? AiBrand.PlusAi : AiBrand.Tab32) + " sertifikaları";
            certificateMode.SelectedIndex = (int)(certificateProvider == AiProvider.Central
                ? draft.CentralCertificateTrustMode : draft.LegacyCertificateTrustMode);
            UpdateCertificateInfo();
        }

        private async void SelectServerCertificate(object sender, EventArgs args)
        {
            if (selectingCertificate || saving) return;
            using (var picker = new OpenFileDialog
            {
                Title = CurrentCertificateMode == CertificateTrustMode.CertificateBundle
                    ? "Kök, ara ve isteğe bağlı sunucu sertifikası paketini seçin"
                    : CurrentCertificateMode == CertificateTrustMode.CertificateAuthority
                    ? "CA kök ve ara sertifikalarını seçin" : "Sunucu ve ara/kök sertifikalarını seçin",
                Filter = "Sertifikalar (*.cer;*.crt)|*.cer;*.crt",
                CheckFileExists = true, Multiselect = true
            })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                selectingCertificate = true;
                SetCertificateBusy(true);
                var mode = CurrentCertificateMode;
                string[] paths = picker.FileNames;
                try
                {
                    var certificate = await ServerCertificateTrust.FromFilesAsync(paths, mode, lifetime.Token);
                    if (resourcesDisposed) return;
                    SetCurrentCertificateData(certificate.EncodedCertificate);
                    certificateInfo.Text = certificate.Description;
                }
                catch (OperationCanceledException) { }
                catch (AiServiceException ex)
                {
                    if (!resourcesDisposed) MessageBox.Show(ex.Message, "Sunucu sertifikası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception)
                {
                    if (!resourcesDisposed) MessageBox.Show("Sertifika kontrolü tamamlanamadı. Dosyaları ve Windows sertifika altyapısını kontrol edin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { selectingCertificate = false; if (!resourcesDisposed) SetCertificateBusy(false); }
            }
        }

        private async void UpdateCertificateInfo()
        {
            if (resourcesDisposed) return;
            int version = ++certificateInfoVersion;
            if (certificateInfoCancellation != null) { certificateInfoCancellation.Cancel(); certificateInfoCancellation.Dispose(); certificateInfoCancellation = null; }
            help.SetToolTip(certificateMode, CurrentCertificateMode == CertificateTrustMode.CertificateBundle
                ? "Kök, ara ve sunucu sertifikaları yüklenebilir. İptal edilmiş sertifikalar denetlenmez."
                : CurrentCertificateMode == CertificateTrustMode.CertificateAuthority
                ? "CA kök/ara zincirine güvenilir; aynı kökten yenilenen sunucu sertifikaları kabul edilir."
                : "Sunucu sertifikası birebir eşleşmelidir; yenilenince tekrar seçin.");
            if (string.IsNullOrEmpty(CurrentCertificateData))
            {
                certificateInfo.Text = "Sertifika seçilmedi. Sistem sertifika doğrulaması kullanılır.";
                return;
            }
            certificateInfo.Text = "Sertifika kontrol ediliyor…";
            if (!IsHandleCreated) return; // Start native work only after the UI context is installed.
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            certificateInfoCancellation = cancellation;
            string data = CurrentCertificateData;
            var mode = CurrentCertificateMode;
            try
            {
                var certificate = await ServerCertificateTrust.FromBase64Async(data, mode, cancellation.Token);
                if (!resourcesDisposed && version == certificateInfoVersion) certificateInfo.Text = certificate.Description;
            }
            catch (OperationCanceledException) { }
            catch (AiServiceException ex)
            {
                if (!resourcesDisposed && version == certificateInfoVersion) certificateInfo.Text = ex.Message;
            }
            catch (Exception)
            {
                if (!resourcesDisposed && version == certificateInfoVersion) certificateInfo.Text = "Sertifika kontrolü tamamlanamadı. Yeniden seçin veya temizleyin.";
            }
        }

        private void SetCertificateBusy(bool busy)
        {
            selectCertificate.Enabled = !busy;
            clearCertificate.Enabled = !busy;
            certificateMode.Enabled = !busy;
            provider.Enabled = !busy;
            saveButton.Enabled = !busy;
        }

        private void SetCurrentCertificateData(string data)
        {
            if (certificateProvider == AiProvider.Central)
            {
                if (CurrentCertificateMode == CertificateTrustMode.CertificateBundle) draft.CentralBundleCertificates = data;
                else if (CurrentCertificateMode == CertificateTrustMode.CertificateAuthority) draft.CentralCaCertificates = data;
                else draft.CentralServerCertificate = data;
                return;
            }
            if (CurrentCertificateMode == CertificateTrustMode.CertificateBundle) draft.LegacyBundleCertificates = data;
            else if (CurrentCertificateMode == CertificateTrustMode.CertificateAuthority) draft.LegacyCaCertificates = data;
            else draft.LegacyServerCertificate = data;
        }

        private async void SaveSettings(object sender, EventArgs e)
        {
            if (saving || selectingCertificate || resourcesDisposed) return;
            saving = true;
            settingsTabs.Enabled = false;
            saveButton.Enabled = false;
            try
            {
                if (!string.IsNullOrEmpty(CurrentCertificateData))
                    await ServerCertificateTrust.FromBase64Async(CurrentCertificateData, CurrentCertificateMode, lifetime.Token);
                if (resourcesDisposed) return;
                draft.Provider = (AiProvider)provider.SelectedItem;
                draft.Model = model.SelectedItem as string ?? "";
                draft.Token = token.Text.Trim();
                draft.UseTls12 = tls12.Checked;
                draft.ApplyMarkdown = applyMarkdown.Checked;
                draft.RibbonPreset = (AiRibbonPreset)ribbonPreset.SelectedIndex;
                draft.MemoryEnabled = memoryEnabled.Checked;
                draft.MemoryText = memoryText.Text;
                var saveCancellation = lifetime.Token;
                await System.Threading.Tasks.Task.Run(() => draft.Save(), saveCancellation);
                if (resourcesDisposed) return;
                RibbonPresetSaved?.Invoke((AiRibbonPreset)ribbonPreset.SelectedIndex);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException ex) when (ex.ParamName == nameof(AiSettings.MemoryText))
            {
                if (resourcesDisposed) return;
                MessageBox.Show("Hafıza en fazla 4.000 karakter olabilir. Metni kısaltıp yeniden kaydedin.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ((TabControl)Controls.Find("SettingsTabs", true)[0]).SelectedTab = (TabPage)memoryText.Parent.Parent;
                memoryText.Focus();
            }
            catch (AiServiceException ex)
            {
                if (resourcesDisposed) return;
                MessageBox.Show(ex.Message, "Sunucu sertifikası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                || ex is System.Security.Cryptography.CryptographicException)
            {
                if (resourcesDisposed) return;
                MessageBox.Show("Ayarlar kaydedilemedi. Kullanıcı profilinin erişilebilir olduğunu kontrol edin.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                if (!resourcesDisposed) MessageBox.Show("Ayar işlemi tamamlanamadı. Ayarları yeniden açıp kayıtlı değerleri kontrol edin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                saving = false;
                if (!resourcesDisposed) { settingsTabs.Enabled = true; saveButton.Enabled = true; }
            }
        }
    }
}
