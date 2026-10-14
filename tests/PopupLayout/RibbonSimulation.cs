using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using Askai;

// Test boundary for Office/VSTO. Production ribbon classes run unchanged;
// actual Office rendering, XML caching and COM context lifetime still require Outlook.
namespace Microsoft.Office.Core
{
    public enum RibbonControlSize { RibbonControlSizeRegular, RibbonControlSizeLarge }
}

namespace Microsoft.Office.Tools.Ribbon
{
    public enum RibbonControlIdType { Office }
    public class RibbonControlId
    {
        public RibbonControlIdType ControlIdType { get; set; }
        public string OfficeId { get; set; }
    }
    public class RibbonUIEventArgs : EventArgs { }
    public delegate void RibbonUIEventHandler(object sender, RibbonUIEventArgs args);
    public delegate void RibbonControlEventHandler(object sender, RibbonControlEventArgs args);
    public class RibbonControlEventArgs : EventArgs
    {
        public RibbonContext Control { get; set; }
    }
    public class RibbonContext { public object Context { get; set; } }
    public class RibbonControl
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public bool Visible { get; set; } = true;
        public Microsoft.Office.Core.RibbonControlSize ControlSize { get; set; }
        public string OfficeImageId { get; set; }
        public Image Image { get; set; }
        public bool ShowImage { get; set; }
        public string ScreenTip { get; set; }
        public string SuperTip { get; set; }
        public void SuspendLayout() { }
        public void ResumeLayout(bool performLayout = true) { }
        public void PerformLayout() { }
    }
    public class RibbonButton : RibbonControl
    {
        public event RibbonControlEventHandler Click;
        public void ClickWithContext(object context)
        {
            Click?.Invoke(this, new RibbonControlEventArgs { Control = new RibbonContext { Context = context } });
        }
    }
    public class RibbonSeparator : RibbonControl { }
    public class RibbonMenu : RibbonControl { public List<RibbonControl> Items { get; } = new List<RibbonControl>(); }
    public class RibbonGroup : RibbonControl { public List<RibbonControl> Items { get; } = new List<RibbonControl>(); }
    public class RibbonTab : RibbonControl
    {
        public RibbonControlId ControlId { get; } = new RibbonControlId();
        public List<RibbonGroup> Groups { get; } = new List<RibbonGroup>();
    }
    public class RibbonFactory
    {
        public RibbonButton CreateRibbonButton() { return new RibbonButton(); }
        public RibbonMenu CreateRibbonMenu() { return new RibbonMenu(); }
        public RibbonGroup CreateRibbonGroup() { return new RibbonGroup(); }
        public RibbonTab CreateRibbonTab() { return new RibbonTab(); }
        public RibbonSeparator CreateRibbonSeparator() { return new RibbonSeparator(); }
    }
    public class RibbonBase : Component
    {
        public RibbonBase(RibbonFactory factory) { Factory = factory; }
        public RibbonFactory Factory { get; }
        public string Name { get; set; }
        public string RibbonType { get; set; }
        public List<RibbonTab> Tabs { get; } = new List<RibbonTab>();
        public event RibbonUIEventHandler Load;
        public event EventHandler Close;
        public void RaiseLoad() { Load?.Invoke(this, new RibbonUIEventArgs()); }
        public void RaiseClose() { Close?.Invoke(this, EventArgs.Empty); }
        public void SuspendLayout() { }
        public void ResumeLayout(bool performLayout = true) { }
    }
}

namespace Ask.ai
{
    internal static class Globals
    {
        internal static readonly TestOfficeFactory Factory = new TestOfficeFactory();
    }
    internal sealed class TestOfficeFactory
    {
        internal Microsoft.Office.Tools.Ribbon.RibbonFactory GetRibbonFactory()
        {
            return new Microsoft.Office.Tools.Ribbon.RibbonFactory();
        }
    }
    internal static class MailActionRunner
    {
        internal static MailCapability LastCapability;
        internal static object LastContext;
        internal static Task RunAsync(MailCapability capability, object context)
        {
            LastCapability = capability;
            LastContext = context;
            return Task.CompletedTask;
        }
    }
}
