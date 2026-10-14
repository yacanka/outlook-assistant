using System.Drawing;

namespace Ask.ai
{
    internal static class AssistantIcon
    {
        // Process-lifetime resources are shared by the ribbons and popup windows.
        internal static readonly Icon WindowIcon = LoadIcon();
        internal static readonly Bitmap RibbonImage = CreateRibbonImage();
        internal static readonly Bitmap LargeRibbonImage = WindowIcon.ToBitmap();

        private static Icon LoadIcon()
        {
            using (var stream = typeof(AssistantIcon).Assembly.GetManifestResourceStream("Ask.ai.Assets.Assistant.ico"))
            using (var icon = new Icon(stream, 32, 32))
                return (Icon)icon.Clone();
        }

        private static Bitmap CreateRibbonImage()
        {
            using (var icon = new Icon(WindowIcon, 16, 16))
                return icon.ToBitmap();
        }
    }
}
