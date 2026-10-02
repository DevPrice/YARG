using System;
using System.IO;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Settings;

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Registers LocalState\Songs as a song folder on a fresh install, so songs copied there are found
    /// without browsing for the folder first.
    /// </summary>
    public static class XboxSongsFolder
    {
        /// <summary>
        /// Call right after <see cref="SettingsManager.LoadSettings"/>, before anything can save settings.
        /// </summary>
        public static void RegisterOnFirstRun()
        {
            // settings.json is only written by SettingsManager.SaveSettings, so its absence marks a first run
            // or a settings reset. Keeping the "already registered" state in the same file as SongFolders means
            // the two can't disagree: the save that records the user removing the folder also ends the first run,
            // and a run that never saved will simply register it again.
            if (File.Exists(Path.Combine(PathHelper.PersistentDataPath, "settings.json")))
            {
                return;
            }

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
