using Ask.ai;
using System;

namespace Ask.ai
{
    public partial class ThisAddIn
    {
        /// <summary>
        /// Ribbon'ı oluşturur ve Outlook'a kaydeder.
        /// </summary>
        protected override Microsoft.Office.Core.IRibbonExtensibility
            CreateRibbonExtensibilityObject()
        {
            return Globals.Factory.GetRibbonFactory()
                .CreateRibbonManager(
                    new Microsoft.Office.Tools.Ribbon.IRibbonExtension[]
                    {
                        new AiMailRibbon(),
                        new AiReadRibbon(),
                        new AiComposeRibbon()
                    });
        }

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            // Eklenti başlatıldığında
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            // Eklenti kapatıldığında
        }

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}