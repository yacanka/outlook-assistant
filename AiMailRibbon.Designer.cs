
using Ask.ai;
using Microsoft.Office.Tools.Ribbon;

namespace Ask.ai
{
    partial class AiMailRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        private System.ComponentModel.IContainer components = null;

        public AiMailRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
            InitializeAiSettingsButton();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.tabMail = this.Factory.CreateRibbonTab();
            this.grpAiAssistant = this.Factory.CreateRibbonGroup();
            this.btnAiReply = this.Factory.CreateRibbonButton();
            this.separator1 = this.Factory.CreateRibbonSeparator();
            this.btnAiReplyAll = this.Factory.CreateRibbonButton();
            this.tabMail.SuspendLayout();
            this.grpAiAssistant.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabMail
            // 
            this.tabMail.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tabMail.ControlId.OfficeId = "TabMail";
            this.tabMail.Groups.Add(this.grpAiAssistant);
            this.tabMail.Label = "TabMail";
            this.tabMail.Name = "tabMail";
            // 
            // grpAiAssistant
            // 
            this.grpAiAssistant.Items.Add(this.btnAiReply);
            this.grpAiAssistant.Items.Add(this.separator1);
            this.grpAiAssistant.Items.Add(this.btnAiReplyAll);
            this.grpAiAssistant.Label = "AI Asistan";
            this.grpAiAssistant.Name = "grpAiAssistant";
            // 
            // btnAiReply
            // 
            this.btnAiReply.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnAiReply.Label = "AI ile\nYanıtla";
            this.btnAiReply.Name = "btnAiReply";
            this.btnAiReply.OfficeImageId = "Reply";
            this.btnAiReply.ScreenTip = "AI ile Yanıtla";
            this.btnAiReply.ShowImage = true;
            this.btnAiReply.SuperTip = "Seçili e-postanın içeriğini yapay zekaya gönderir ve gelen yanıtı Reply olarak hazırlar.";
            this.btnAiReply.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnAiReply_Click);
            // 
            // separator1
            // 
            this.separator1.Name = "separator1";
            // 
            // btnAiReplyAll
            // 
            this.btnAiReplyAll.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnAiReplyAll.Label = "AI ile\nTümünü Yanıtla";
            this.btnAiReplyAll.Name = "btnAiReplyAll";
            this.btnAiReplyAll.OfficeImageId = "ReplyAll";
            this.btnAiReplyAll.ScreenTip = "AI ile Tümünü Yanıtla";
            this.btnAiReplyAll.ShowImage = true;
            this.btnAiReplyAll.SuperTip = "Seçili e-postanın içeriğini yapay zekaya gönderir ve gelen yanıtı Reply All olarak hazırlar.";
            this.btnAiReplyAll.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnAiReplyAll_Click);
            // 
            // AiMailRibbon
            // 
            this.Name = "AiMailRibbon";
            this.RibbonType = "Microsoft.Outlook.Explorer";
            this.Tabs.Add(this.tabMail);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.AiMailRibbon_Load);
            this.tabMail.ResumeLayout(false);
            this.tabMail.PerformLayout();
            this.grpAiAssistant.ResumeLayout(false);
            this.grpAiAssistant.PerformLayout();
            this.ResumeLayout(false);

        }

        // ── Kontrol tanımları ──
        internal Microsoft.Office.Tools.Ribbon.RibbonTab tabMail;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup grpAiAssistant;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnAiReply;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnAiReplyAll;
        internal Microsoft.Office.Tools.Ribbon.RibbonSeparator separator1;
    }
}






