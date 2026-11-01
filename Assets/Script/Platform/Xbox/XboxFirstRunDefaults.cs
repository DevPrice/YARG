using System.IO;
using YARG.Helpers;
using YARG.Settings;

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Xbox defaults for a fresh install or a settings reset.
    /// </summary>
    public static class XboxFirstRunDefaults
    {
        // Twice the console's 120 Hz output, so each refresh shows a frame at most ~4 ms old
        private const int FPS_CAP = 240;

        /// <summary>
        /// Call right after <see cref="SettingsManager.LoadSettings"/>, before anything can save settings.
        /// </summary>
        public static void Apply()
        {
            // settings.json is only written by SettingsManager.SaveSettings, so its absence marks a first run
            // or a settings reset. Keeping the "already applied" state in the same file as the settings means
            // the two can't disagree: the save that records the user changing one also ends the first run, and
            // a run that never saved will simply apply them again.
            if (File.Exists(Path.Combine(PathHelper.PersistentDataPath, "settings.json")))
            {
                return;
            }

            XboxSongsFolder.Register();

            // A UWP app on Xbox presents at 60 Hz with VSync on even when the output runs at 120 Hz (measured on
            // D3D11 and D3D12, and with DXGIDisableVBlankVirtualization). With VSync off the console doesn't tear;
            // it shows the newest frame at each refresh.
            SettingsManager.Settings.VSync.Value = false;
            SettingsManager.Settings.FpsCap.Value = FPS_CAP;
        }
    }
}
