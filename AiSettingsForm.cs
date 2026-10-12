using System;
using System.Drawing;
using System.Windows.Forms;

namespace Askai
{
    public sealed class AiSettingsForm : Form
    {
        private readonly ComboBox provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
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
        private readonly Button clearCertificate = new Button { Text = "Temizle", AutoSize = true };
        private readonly Button clearToken = new Button { Name = "ClearToken", Text = "Token’ı sil", AutoSize = true };
        private string centralServerCertificate = "";
        private string centralCaCertificates = "";
        private string centralBundleCertificates = "";
        private CertificateTrustMode CurrentCertificateMode => certificateMode.SelectedIndex == 2
            ? CertificateTrustMode.CertificateBundle : certificateMode.SelectedIndex == 1
                ? CertificateTrustMode.CertificateAuthority : CertificateTrustMode.ServerCertificate;
        private string CurrentCertificateData => CurrentCertificateMode == CertificateTrustMode.CertificateBundle
            ? centralBundleCertificates : CurrentCertificateMode == CertificateTrustMode.CertificateAuthority
                ? centralCaCertificates : centralServerCertificate;

        public AiSettingsForm()
        {
            Text = AiBrand.Name + " — Ayarlar";
            Icon = Ask.ai.AssistantIcon.WindowIcon;
            Font = new Font("Segoe UI", 10f);
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 620);
            MinimumSize = SizeFromClientSize(new Size(400, 300));
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new Ask.ai.ResponsiveTableLayoutPanel
            {
                AutoScroll = false, Padding = new Padding(20), ColumnCount = 1, RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = AiBrand.Name, AutoSize = true, Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 18f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 16)
            }, 0, 0);
            var tabs = new TabControl { Name = "SettingsTabs", Dock = DockStyle.Fill, Padding = new Point(14, 7), TabIndex = 0 };
            var servicePage = new TabPage("Servis") { BackColor = SystemColors.Window };
            var connectionPage = new TabPage("Bağlantı ve sertifika") { BackColor = SystemColors.Window };
            tabs.TabPages.Add(servicePage);
            tabs.TabPages.Add(connectionPage);
            var formattingPage = new TabPage("Mail biçimlendirme") { BackColor = SystemColors.Window };
            tabs.TabPages.Add(formattingPage);
            var formattingLayout = CreatePageLayout();
            AddRow(formattingLayout, applyMarkdown);
            help.SetToolTip(applyMarkdown, "Başlık, vurgu, liste ve tabloları önizlemede ve Outlook’ta biçimlendirir.");
            formattingPage.Controls.Add(formattingLayout);
            layout.Controls.Add(tabs, 0, 1);
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
            layout.Controls.Add(actions, 0, 2);
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
            provider.SelectedItem = settings.Provider;
            model.SelectedItem = settings.Model;
            if (model.SelectedIndex < 0 && model.Items.Count > 0) model.SelectedIndex = 0;
            token.Text = settings.Token;
            tls12.Checked = settings.UseTls12;
            applyMarkdown.Checked = settings.ApplyMarkdown;
            memoryEnabled.Checked = settings.MemoryEnabled;
            memoryText.Text = settings.MemoryText;
            UpdateMemoryCount();
            centralServerCertificate = settings.CentralServerCertificate;
            centralCaCertificates = settings.CentralCaCertificates;
            centralBundleCertificates = settings.CentralBundleCertificates;
            certificateMode.SelectedIndex = (int)settings.CentralCertificateTrustMode;
            certificateMode.SelectedIndexChanged += (sender, args) => UpdateCertificateInfo();
            UpdateCertificateInfo();
            provider.SelectedIndexChanged += (s, e) => UpdateEnabled();
            UpdateEnabled();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) help.Dispose();
            base.Dispose(disposing);
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
            AddRow(layout, SectionLabel("PLUS AI erişimi"));
            AddRow(layout, FieldLabel("&Model"));
            AddRow(layout, model);
            AddRow(layout, FieldLabel("&Erişim token’ı"));
            AddRow(layout, token);
            StyleButton(clearToken);
            clearToken.Click += (sender, args) => token.Clear();
            var tokenActions = CreateButtonRow();
            tokenActions.Controls.Add(clearToken);
            AddRow(layout, tokenActions);
            help.SetToolTip(token, "Bu Windows kullanıcısı için şifreli saklanır.");
            help.SetToolTip(clearToken, "Token’ı silmek için temizleyip Kaydet’e basın.");
            page.Controls.Add(layout);
        }

        private void BuildConnectionPage(TabPage page)
        {
            var layout = CreatePageLayout();
            AddRow(layout, tls12);
            help.SetToolTip(tls12, "TLS bağlantı hatasında deneyin. Sertifika doğrulaması açık kalır.");
            AddRow(layout, SectionLabel("PLUS AI sertifikaları"));
            AddRow(layout, FieldLabel("&Sertifika modu"));
            certificateMode.Name = "CertificateMode";
            certificateMode.AccessibleName = "PLUS AI sertifika modu";
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
            var layout = new Ask.ai.ResponsiveTableLayoutPanel { Padding = new Padding(16), ColumnCount = 1, RowCount = 0 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.TabIndex = row;
            control.Margin = new Padding(0, 3, 0, 7);
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
            model.Enabled = central;
            token.Enabled = central;
            certificateInfo.Enabled = central;
            certificateMode.Enabled = central;
            selectCertificate.Enabled = central;
            clearCertificate.Enabled = central;
        }

        private void SelectServerCertificate(object sender, EventArgs args)
        {
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
                try
                {
                    var certificate = ServerCertificateTrust.FromFiles(picker.FileNames, CurrentCertificateMode);
                    SetCurrentCertificateData(certificate.EncodedCertificate);
                    certificateInfo.Text = certificate.Description;
                }
                catch (AiServiceException ex)
                {
                    MessageBox.Show(ex.Message, "Sunucu sertifikası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void UpdateCertificateInfo()
        {
            help.SetToolTip(certificateMode, CurrentCertificateMode == CertificateTrustMode.CertificateBundle
                ? "Kök, ara ve sunucu sertifikaları yüklenebilir. İptal edilmiş sertifikalar denetlenmez."
                : CurrentCertificateMode == CertificateTrustMode.CertificateAuthority
                ? "CA kök/ara zincirine güvenilir; aynı kökten yenilenen sunucu sertifikaları kabul edilir."
                : "Sunucu sertifikası birebir eşleşmelidir; yenilenince tekrar seçin.");
            if (string.IsNullOrEmpty(CurrentCertificateData))
            {
                certificateInfo.Text = "Sertifika seçilmedi.";
                return;
            }
            try { certificateInfo.Text = ServerCertificateTrust.FromBase64(CurrentCertificateData, CurrentCertificateMode).Description; }
            catch (AiServiceException) { certificateInfo.Text = "Kaydedilmiş sertifika geçersiz veya süresi dolmuş. Yeniden seçin veya temizleyin."; }
        }

        private void SetCurrentCertificateData(string data)
        {
            if (CurrentCertificateMode == CertificateTrustMode.CertificateBundle) centralBundleCertificates = data;
            else if (CurrentCertificateMode == CertificateTrustMode.CertificateAuthority) centralCaCertificates = data;
            else centralServerCertificate = data;
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            try
            {
                if ((AiProvider)provider.SelectedItem == AiProvider.Central && !string.IsNullOrEmpty(CurrentCertificateData))
                    ServerCertificateTrust.FromBase64(CurrentCertificateData, CurrentCertificateMode);
                new AiSettings { Provider = (AiProvider)provider.SelectedItem,
                    Model = model.SelectedItem as string ?? "", Token = token.Text.Trim(),
                    UseTls12 = tls12.Checked, ApplyMarkdown = applyMarkdown.Checked,
                    MemoryEnabled = memoryEnabled.Checked, MemoryText = memoryText.Text,
                    CentralServerCertificate = centralServerCertificate,
                    CentralCaCertificates = centralCaCertificates, CentralBundleCertificates = centralBundleCertificates,
                    CentralCertificateTrustMode = CurrentCertificateMode }.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (ArgumentException ex) when (ex.ParamName == nameof(AiSettings.MemoryText))
            {
                MessageBox.Show("Hafıza en fazla 4.000 karakter olabilir. Metni kısaltıp yeniden kaydedin.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ((TabControl)Controls.Find("SettingsTabs", true)[0]).SelectedTab = (TabPage)memoryText.Parent.Parent;
                memoryText.Focus();
            }
            catch (AiServiceException ex)
            {
                MessageBox.Show(ex.Message, "Sunucu sertifikası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                || ex is System.Security.Cryptography.CryptographicException)
            {
                MessageBox.Show("Ayarlar kaydedilemedi. Kullanıcı profilinin erişilebilir olduğunu kontrol edin.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
