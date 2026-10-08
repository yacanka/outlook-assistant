using Askai;
using Microsoft.Office.Tools.Ribbon;
using System;
using System.Windows.Forms;

namespace Ask.ai
{
    public partial class AiMailRibbon
    {
        private void AiMailRibbon_Load(object sender, RibbonUIEventArgs e) { }

        // Called by the existing designer constructor. Keep new controls in handwritten code.
        private void InitializeAiSettingsButton()
        {
            var compose = Factory.CreateRibbonButton();
            compose.Name = "btnAiCompose";
            compose.Label = "AI ile mail hazırla";
            compose.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            compose.OfficeImageId = "NewMailMessage";
            compose.ShowImage = true;
            compose.ScreenTip = "Konudan yeni mail hazırla";
            compose.SuperTip = "Konu ve talimatınızdan içerik hazırlar. Onayınızla yeni mail açar; mail göndermez.";
            compose.Click += async (sender, args) => await MailActionRunner.RunAsync(MailCapability.Compose, args.Control.Context);
            grpAiAssistant.Items.Insert(0, compose);
            btnAiReply.SuperTip = "Seçili mail için yanıt hazırlar. Onayınızdan sonra Outlook yanıt taslağını açar; mail göndermez.";
            btnAiReplyAll.SuperTip = "Seçili mail için tümünü yanıtla taslağı hazırlar. Aktarım için onayınızı bekler; mail göndermez.";
            var settings = Factory.CreateRibbonButton();
            settings.Label = "AI Ayarları";
            settings.Name = "btnAiSettings";
            settings.Click += (sender, args) => { using (var dialog = new AiSettingsForm()) dialog.ShowDialog(); };
            grpAiAssistant.Items.Add(settings);
        }

        private async void btnAiReply_Click(object sender, RibbonControlEventArgs e)
        {
            await MailActionRunner.RunAsync(MailCapability.Reply, e.Control.Context);
        }

        private async void btnAiReplyAll_Click(object sender, RibbonControlEventArgs e)
        {
            await MailActionRunner.RunAsync(MailCapability.ReplyAll, e.Control.Context);
        }
    }
}
