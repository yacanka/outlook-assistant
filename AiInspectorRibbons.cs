using Askai;
using Microsoft.Office.Tools.Ribbon;
using System.Windows.Forms;

namespace Ask.ai
{
    // Separate ribbon types avoid referencing Explorer tabs from Inspector windows.
    internal sealed class AiReadRibbon : AiInspectorRibbon
    {
        public AiReadRibbon() : base(false) { }
    }

    internal sealed class AiComposeRibbon : AiInspectorRibbon
    {
        public AiComposeRibbon() : base(true) { }
    }

    internal abstract class AiInspectorRibbon : RibbonBase
    {
        protected AiInspectorRibbon(bool composing) : base(Globals.Factory.GetRibbonFactory())
        {
            Name = composing ? "AiComposeRibbon" : "AiReadRibbon";
            RibbonType = composing ? "Microsoft.Outlook.Mail.Compose" : "Microsoft.Outlook.Mail.Read";
            var tab = Factory.CreateRibbonTab();
            tab.Name = composing ? "tabAiCompose" : "tabAiRead";
            tab.ControlId.ControlIdType = RibbonControlIdType.Office;
            tab.ControlId.OfficeId = composing ? "TabNewMailMessage" : "TabReadMessage";
            var group = Factory.CreateRibbonGroup();
            group.Name = composing ? "grpAiCompose" : "grpAiRead";
            group.Label = AiBrand.Name;
            tab.Groups.Add(group);
            Tabs.Add(tab);
            RibbonButton reply = null;
            RibbonButton replyAll = null;
            if (composing)
                AddCapability(group, "btnAiImprove", "Metni İyileştir", "Spelling", MailCapability.Improve,
                    "Taslakta seçtiğiniz metni bağlamını koruyarak iyileştirir. Onayınızla yalnızca seçilen alanı değiştirir.");
            else
            {
                reply = AddCapability(group, "btnAiReadReply", "Yanıtla", "Reply", MailCapability.Reply,
                    "Bu mail için yanıt hazırlar. Onayınızla Outlook taslağı açılır; mail gönderilmez.");
                replyAll = AddCapability(group, "btnAiReadReplyAll", "Tümünü Yanıtla", "ReplyAll", MailCapability.ReplyAll,
                    "Bu mail için tümünü yanıtla taslağı hazırlar. Onayınız olmadan aktarım yapılmaz.", large: false);
            }
            new AiRibbonLayout(this, group, reply, replyAll);
        }

        private RibbonButton AddCapability(RibbonGroup group, string name, string label, string image,
            MailCapability capability, string description, bool large = true)
        {
            var button = Factory.CreateRibbonButton();
            button.Name = name;
            button.Label = label;
            button.ControlSize = large ? Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge
                : Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeRegular;
            button.OfficeImageId = image;
            button.ShowImage = true;
            button.ScreenTip = label;
            button.SuperTip = description;
            button.Click += async (sender, args) => await MailActionRunner.RunAsync(capability, args.Control.Context);
            group.Items.Add(button);
            return button;
        }
    }
}
