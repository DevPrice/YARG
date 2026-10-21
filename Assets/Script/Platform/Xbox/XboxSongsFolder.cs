using System;
using System.IO;
using YARG.Core.Logging;
using YARG.Settings;

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Registers LocalState\Songs as a song folder, so songs copied there are found without browsing for the
    /// folder first.
    /// </summary>
    public static class XboxSongsFolder
    {
        public static void Register()
        {
            string folder = XboxFileBrowserQuickLinks.SongsFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to create the Songs folder.");
                return;
            }

            var songFolders = SettingsManager.Settings.SongFolders;
            if (!songFolders.Contains(folder))
            {
                songFolders.Add(folder);
                YargLogger.LogFormatInfo("Registered {0} as a song folder", folder);
            }
        }
    }
}
