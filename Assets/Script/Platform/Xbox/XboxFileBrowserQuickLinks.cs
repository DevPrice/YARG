using System;
using System.Collections.Generic;
using System.IO;
using SimpleFileBrowser;
using YARG.Core.Logging;
using YARG.Helpers;

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Folder-picker quick links for the console, where SimpleFileBrowser itself only links LocalState.
    /// </summary>
    public static class XboxFileBrowserQuickLinks
    {
        public static string SongsFolder => Path.Combine(PathHelper.RealPersistentDataPath, "Songs");

        private static readonly HashSet<char> _reportedDrives = new();

        /// <summary>
        /// Adds the LocalState Songs folder and any readable drive roots. Call only after the scene's
        /// <see cref="FileBrowser"/> has been activated: before its Awake, <see cref="FileBrowser.Instance"/>
        /// instantiates a second browser from Resources.
        /// </summary>
        public static void Add()
        {
            try
            {
                Directory.CreateDirectory(SongsFolder);
                FileBrowser.AddQuickLink("Songs", SongsFolder);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to add the Songs folder quick link.");
            }

            // Re-probed on every open so a USB drive plugged in after launch shows up.
            for (char letter = 'D'; letter <= 'Z'; letter++)
            {
                TryAddDrive(letter);
            }
        }

        private static void TryAddDrive(char letter)
        {
            string root = letter + @":\";
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }

                // Without the removableStorage or broadFileSystemAccess capability, a drive can exist
                // but deny listing, which would leave the picker on an empty, unusable root.
                using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator())
                {
                    entries.MoveNext();
                }

                FileBrowser.AddQuickLink($"Drive ({letter}:)", root);
            }
            catch (Exception e)
            {
                if (_reportedDrives.Add(letter))
                {
                    YargLogger.LogFormatWarning("Drive {0} is not accessible: {1}", root, e.Message);
                }
            }
        }
    }
}
