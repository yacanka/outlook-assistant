using System;
using System.Linq;
using System.Reflection;
using Ask.ai;
using Askai;
using Microsoft.Office.Core;
using Microsoft.Office.Tools.Ribbon;

internal static class RibbonLayoutTests
{
    internal static void Run()
    {
        using (var explorer = new AiMailRibbon())
        using (var read = new AiReadRibbon())
        using (var compose = new AiComposeRibbon())
        {
            foreach (var ribbon in new RibbonBase[] { explorer, read, compose }) ribbon.RaiseLoad();
            NotifyPreset(AiRibbonPreset.Standard);
            var group = explorer.Tabs.Single().Groups.Single();
            Check(group.Label == "AI Asistan", "All Explorer actions belong to the AI Asistan group.");
            Check(string.Join(",", group.Items.Where(x => x.Visible).Select(x => x.Label)) == "Mail Hazırla,Özetle,Yanıtla,Tümünü Yanıtla,Ayarlar",
                "Preset 1 shows the compose, separate replies and settings in order.");
            Check(group.Items.Single(x => x.Label == "Mail Hazırla").ControlSize == RibbonControlSize.RibbonControlSizeLarge,
                "Compose stays large in preset 1.");
            Check(group.Items.Where(x => x.Visible && x.Label != "Mail Hazırla").All(x => x.ControlSize == RibbonControlSize.RibbonControlSizeRegular),
                "Preset 1 replies and settings stay small.");
            NotifyPreset(AiRibbonPreset.RepliesMenu);
            Check(string.Join(",", group.Items.Where(x => x.Visible).Select(x => x.Label)) == "Mail Hazırla,Özetle,Yanıtla,Ayarlar"
                && group.Items.Where(x => x.Visible && x.Label != "Özetle").All(x => x.ControlSize == RibbonControlSize.RibbonControlSizeLarge),
                "Preset 2 keeps compose, reply menu and settings large alongside summary.");
            Check(group.Items.Single(x => x.Label == "Ayarlar").Image.Width == 32, "Large settings controls use the full-size icon.");
            var menu = group.Items.OfType<RibbonMenu>().Single();
            Check(string.Join(",", menu.Items.Select(x => x.Label)) == "Yanıtla,Tümünü Yanıtla", "The reply menu has both options.");
            var context = new object();
            ((RibbonButton)group.Items.Single(x => x.Label == "Özetle")).ClickWithContext(context);
            Check(MailActionRunner.LastCapability == MailCapability.Summarize && ReferenceEquals(MailActionRunner.LastContext, context),
                "Summary forwards selected window context.");
            ((RibbonButton)menu.Items[0]).ClickWithContext(context);
            Check(MailActionRunner.LastCapability == MailCapability.Reply && ReferenceEquals(MailActionRunner.LastContext, context),
                "The menu reply action forwards its own Office context.");
            ((RibbonButton)menu.Items[1]).ClickWithContext(context);
            Check(MailActionRunner.LastCapability == MailCapability.ReplyAll && ReferenceEquals(MailActionRunner.LastContext, context),
                "The menu reply-all action forwards its own Office context.");
            var readGroup = read.Tabs.Single().Groups.Single();
            Check(string.Join(",", readGroup.Items.Where(x => x.Visible).Select(x => x.Label)) == "Özetle,Yanıtla,Ayarlar",
                "Read Inspectors apply the reply menu preset without unrelated compose actions.");
            Check(string.Join(",", compose.Tabs.Single().Groups.Single().Items.Where(x => x.Visible).Select(x => x.Label)) == "Metni İyileştir,Ayarlar",
                "Compose Inspectors preserve the text improvement capability.");
            explorer.RaiseClose();
            NotifyPreset(AiRibbonPreset.Standard);
            Check(menu.Visible, "Closed ribbons detach from settings updates.");
            Check(!readGroup.Items.OfType<RibbonMenu>().Single().Visible, "Other open ribbons still update.");
            NotifyPreset(AiRibbonPreset.RepliesMenu);
            read.Dispose();
            NotifyPreset(AiRibbonPreset.Standard);
            Check(readGroup.Items.OfType<RibbonMenu>().Single().Visible, "Disposed ribbons detach from settings updates.");
        }
        Check(typeof(AiSettingsForm).GetField("RibbonPresetSaved", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null,
            "Disposed ribbon instances leave no static event subscriptions.");
        Console.WriteLine("Ribbon simulations passed (presets, action routing, all window types, live updates, lifecycle).");
    }

    private static void NotifyPreset(AiRibbonPreset preset)
    {
        var saved = (Action<AiRibbonPreset>)typeof(AiSettingsForm)
            .GetField("RibbonPresetSaved", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        saved?.Invoke(preset);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
    }
}
