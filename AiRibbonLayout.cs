using Askai;
using Microsoft.Office.Core;
using Microsoft.Office.Tools.Ribbon;
using System;

namespace Ask.ai
{
    // Build both layouts before Office freezes the control tree. Only visibility and size change later.
    internal sealed class AiRibbonLayout
    {
        private readonly RibbonBase ribbon;
        private readonly RibbonButton reply;
        private readonly RibbonButton replyAll;
        private readonly RibbonMenu replyMenu;
        private readonly RibbonButton settings;

        internal AiRibbonLayout(RibbonBase ribbon, RibbonGroup group, RibbonButton reply = null, RibbonButton replyAll = null)
        {
            this.ribbon = ribbon;
            this.reply = reply;
            this.replyAll = replyAll;
            if (reply != null && replyAll != null)
            {
                replyMenu = ribbon.Factory.CreateRibbonMenu();
                replyMenu.Name = group.Name + "ReplyMenu";
                replyMenu.Label = "Yanıtla";
                replyMenu.OfficeImageId = "Reply";
                replyMenu.ShowImage = true;
                replyMenu.ControlSize = RibbonControlSize.RibbonControlSizeLarge;
                replyMenu.Items.Add(CreateReplyOption("Reply", "Yanıtla", "Reply", MailCapability.Reply));
                replyMenu.Items.Add(CreateReplyOption("ReplyAll", "Tümünü Yanıtla", "ReplyAll", MailCapability.ReplyAll));
                group.Items.Add(replyMenu);
            }
            settings = ribbon.Factory.CreateRibbonButton();
            settings.Name = group.Name + "Settings";
            settings.Label = "Ayarlar";
            settings.Image = AssistantIcon.RibbonImage;
            settings.ShowImage = true;
            settings.ScreenTip = "AI Asistan ayarları";
            settings.SuperTip = "Servis, model, token, şerit düzeni ve bağlantı ayarlarını düzenleyin.";
            settings.Click += (sender, args) => { using (var dialog = new AiSettingsForm()) dialog.ShowDialog(); };
            group.Items.Add(settings);
            ApplySavedPreset();
            ribbon.Load += (sender, args) =>
            {
                ApplySavedPreset();
                AiSettingsForm.RibbonPresetSaved -= Apply;
                AiSettingsForm.RibbonPresetSaved += Apply;
            };
            ribbon.Close += (sender, args) => AiSettingsForm.RibbonPresetSaved -= Apply;
            ribbon.Disposed += (sender, args) => AiSettingsForm.RibbonPresetSaved -= Apply;
        }

        private RibbonButton CreateReplyOption(string suffix, string label, string image, MailCapability capability)
        {
            var button = ribbon.Factory.CreateRibbonButton();
            button.Name = replyMenu.Name + suffix;
            button.Label = label;
            button.OfficeImageId = image;
            button.ShowImage = true;
            button.SuperTip = "Yanıtı hazırlar. Onayınızla Outlook taslağı açılır; mail gönderilmez.";
            button.Click += async (sender, args) => await MailActionRunner.RunAsync(capability, args.Control.Context);
            return button;
        }

        private void ApplySavedPreset()
        {
            try { Apply(AiSettings.LoadRibbonPreset()); }
            catch (InvalidOperationException) { Apply(AiRibbonPreset.Standard); }
        }

        private void Apply(AiRibbonPreset preset)
        {
            bool menu = preset == AiRibbonPreset.RepliesMenu;
            ribbon.SuspendLayout();
            try
            {
                settings.ControlSize = menu ? RibbonControlSize.RibbonControlSizeLarge : RibbonControlSize.RibbonControlSizeRegular;
                settings.Image = menu ? AssistantIcon.LargeRibbonImage : AssistantIcon.RibbonImage;
                if (replyMenu == null) return;
                reply.Visible = !menu;
                replyAll.Visible = !menu;
                reply.ControlSize = RibbonControlSize.RibbonControlSizeRegular;
                replyAll.ControlSize = RibbonControlSize.RibbonControlSizeRegular;
                replyMenu.Visible = menu;
            }
            finally { ribbon.ResumeLayout(); }
        }
    }
}
