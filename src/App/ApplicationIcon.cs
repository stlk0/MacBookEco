using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace MacBookEco.App
{
    /// <summary>
    /// The application's own icon, loaded once from the embedded resource.
    ///
    /// The window, the taskbar button and the notification area all used
    /// SystemIcons.Information before this. In the tray that is actively
    /// confusing: a blue circled "i" is what Windows itself uses for
    /// notifications, so the application was indistinguishable from one.
    ///
    /// Loading is best-effort. A missing or unreadable resource falls back to
    /// the old system icon rather than preventing the tray from starting: an
    /// icon is not worth failing a launch over.
    /// </summary>
    internal static class ApplicationIcon
    {
        private const string ResourceName = "MacBookEco.ico";

        private static readonly object Gate = new object();
        private static Icon _icon;
        private static bool _loaded;

        internal static Icon Shared
        {
            get
            {
                lock (Gate)
                {
                    if (!_loaded)
                    {
                        _icon = Load();
                        _loaded = true;
                    }

                    return _icon ?? SystemIcons.Information;
                }
            }
        }

        private static Icon Load()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(ResourceName))
                {
                    return stream == null ? null : new Icon(stream);
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
