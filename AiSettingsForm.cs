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
        private readonly CheckBox tls12 = new CheckBox { Text = "TLS 1.2 uyumluluk modu", AutoSize = true };
        private readonly TextBox certificateInfo = new TextBox
        {
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 84
        };
        private readonly Button selectCertificate = new Button { Text = "Sertifika seç…", AutoSize = true };
        private readonly Button clearCertificate = new Button { Text = "Temizle", AutoSize = true };
        private string centralServerCertificate = "";

        public AiSettingsForm()
        {
            Text = "AI Ayarları";
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 590);
            MinimumSize = SizeFromClientSize(new Size(400, 300));
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new Ask.ai.ResponsiveTableLayoutPanel { Padding = new Padding(16), ColumnCount = 2, RowCount = 11 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            for (int row = 0; row < layout.RowCount; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "Servis", AutoSize = true }, 0, 0);
            layout.Controls.Add(provider, 1, 0);
            layout.Controls.Add(new Label { Text = "Central model", AutoSize = true }, 0, 1);
            layout.Controls.Add(model, 1, 1);
            layout.Controls.Add(new Label { Text = "Central token", AutoSize = true }, 0, 2);
            layout.Controls.Add(token, 1, 2);
            var clear = new Button { Text = "Token'ı sil", AutoSize = true };
            clear.Click += (s, e) => token.Clear();
            layout.Controls.Add(clear, 1, 3);
            layout.Controls.Add(tls12, 1, 4);
            layout.Controls.Add(new Label
            {
                Text = "TLS bağlantı hatasında deneyin. Yalnızca AI bağlantısı TLS 1.2 kullanır; sertifika doğrulaması açık kalır. Kapalıyken mevcut TLS seçimi kullanılır.",
                AutoSize = true, Dock = DockStyle.Fill
            }, 1, 5);
            layout.Controls.Add(new Label { Text = "Central sunucu sertifikası", AutoSize = true, Dock = DockStyle.Fill }, 0, 6);
            layout.Controls.Add(certificateInfo, 1, 6);
            var certificateButtons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            certificateButtons.Controls.Add(selectCertificate);
            certificateButtons.Controls.Add(clearCertificate);
            layout.Controls.Add(certificateButtons, 1, 7);
            selectCertificate.Click += SelectServerCertificate;
            clearCertificate.Click += (sender, args) => { centralServerCertificate = ""; UpdateCertificateInfo(); };
            layout.Controls.Add(new Label
            {
                Text = "İsteğe bağlı .cer/.crt dosyası. Seçildiğinde yalnızca bu sunucu sertifikası kabul edilir; adres ve geçerlilik kontrolleri devam eder. Windows sertifika deposu değişmez.",
                AutoSize = true, Dock = DockStyle.Fill
            }, 1, 8);
            layout.Controls.Add(new Label { Text = "Token bu Windows kullanıcısı için şifrelenir. Silmek için alanı temizleyip kaydedin.", AutoSize = true, Dock = DockStyle.Fill }, 1, 9);
            var save = new Button { Text = "Kaydet", AutoSize = true };
            save.Click += SaveSettings;
            layout.Controls.Add(save, 1, 10);
            Controls.Add(layout);
            AcceptButton = save;
            provider.Items.AddRange(new object[] { AiProvider.Central, AiProvider.Legacy });
            model.Items.AddRange(AppConfig.CentralModels);
            AiSettings settings;
            try { settings = AiSettings.Load(); }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "AI Ayarları", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                settings = new AiSettings();
            }
            provider.SelectedItem = settings.Provider;
            model.SelectedItem = settings.Model;
            if (model.SelectedIndex < 0 && model.Items.Count > 0) model.SelectedIndex = 0;
            token.Text = settings.Token;
            tls12.Checked = settings.UseTls12;
            centralServerCertificate = settings.CentralServerCertificate;
            UpdateCertificateInfo();
            provider.SelectedIndexChanged += (s, e) => UpdateEnabled();
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            bool central = (AiProvider)provider.SelectedItem == AiProvider.Central;
            model.Enabled = central;
            token.Enabled = central;
            certificateInfo.Enabled = central;
            selectCertificate.Enabled = central;
            clearCertificate.Enabled = central;
        }

        private void SelectServerCertificate(object sender, EventArgs args)
        {
            using (var picker = new OpenFileDialog
            {
                Title = "Central sunucusunun sertifikasını seçin",
                Filter = "Sunucu sertifikası (*.cer;*.crt)|*.cer;*.crt",
                CheckFileExists = true, Multiselect = false
            })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var certificate = ServerCertificateTrust.FromFile(picker.FileName);
                    centralServerCertificate = certificate.EncodedCertificate;
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
            if (string.IsNullOrEmpty(centralServerCertificate))
            {
                certificateInfo.Text = "Manuel sertifika seçilmedi. Normal Windows sertifika doğrulaması kullanılır.";
                return;
            }
            try { certificateInfo.Text = ServerCertificateTrust.FromBase64(centralServerCertificate).Description; }
            catch (AiServiceException) { certificateInfo.Text = "Kaydedilmiş sertifika geçersiz veya süresi dolmuş. Yeniden seçin veya temizleyin."; }
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            try
            {
                if ((AiProvider)provider.SelectedItem == AiProvider.Central && !string.IsNullOrEmpty(centralServerCertificate))
                    ServerCertificateTrust.FromBase64(centralServerCertificate);
                new AiSettings { Provider = (AiProvider)provider.SelectedItem,
                    Model = model.SelectedItem as string ?? "", Token = token.Text.Trim(),
                    UseTls12 = tls12.Checked, CentralServerCertificate = centralServerCertificate }.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (AiServiceException ex)
            {
                MessageBox.Show(ex.Message, "Sunucu sertifikası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                || ex is System.Security.Cryptography.CryptographicException)
            {
                MessageBox.Show("AI ayarları kaydedilemedi. Kullanıcı profilinin erişilebilir olduğunu kontrol edin.",
                    "AI Ayarları", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
