using System;
using System.Drawing;
using System.Windows.Forms;

namespace Askai
{
    public sealed class AiSettingsForm : Form
    {
        private readonly ComboBox provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly ComboBox model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly TextBox token = new TextBox { UseSystemPasswordChar = true, Width = 300 };

        public AiSettingsForm()
        {
            Text = "AI Ayarları";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 6 };
            layout.Controls.Add(new Label { Text = "Servis", AutoSize = true }, 0, 0);
            layout.Controls.Add(provider, 1, 0);
            layout.Controls.Add(new Label { Text = "Central model", AutoSize = true }, 0, 1);
            layout.Controls.Add(model, 1, 1);
            layout.Controls.Add(new Label { Text = "Central token", AutoSize = true }, 0, 2);
            layout.Controls.Add(token, 1, 2);
            var clear = new Button { Text = "Token'ı sil", AutoSize = true };
            clear.Click += (s, e) => token.Clear();
            layout.Controls.Add(clear, 1, 3);
            layout.Controls.Add(new Label { Text = "Token bu Windows kullanıcısı için şifrelenir. Silmek için alanı temizleyip kaydedin.", AutoSize = true, MaximumSize = new Size(300, 0) }, 1, 4);
            var save = new Button { Text = "Kaydet", AutoSize = true };
            save.Click += SaveSettings;
            layout.Controls.Add(save, 1, 5);
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
            provider.SelectedIndexChanged += (s, e) => UpdateEnabled();
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            bool central = (AiProvider)provider.SelectedItem == AiProvider.Central;
            model.Enabled = central;
            token.Enabled = central;
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            try
            {
                new AiSettings { Provider = (AiProvider)provider.SelectedItem,
                    Model = model.SelectedItem as string ?? "", Token = token.Text.Trim() }.Save();
                DialogResult = DialogResult.OK;
                Close();
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
