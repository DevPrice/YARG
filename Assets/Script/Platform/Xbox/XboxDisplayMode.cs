#if ENABLE_WINMD_SUPPORT
using System;
using Windows.Foundation;
using Windows.Graphics.Display.Core;
using YARG.Core.Logging;
#endif

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Switches the console's HDMI output to the fastest refresh rate it offers at the current resolution.
    /// A UWP app on Xbox gets a 60 Hz mode unless it asks for another, even when the dashboard runs at 120 Hz,
    /// so VSync would cap the game at 60 fps.
    /// </summary>
    public static class XboxDisplayMode
    {
        public static void RequestHighestRefreshRate()
        {
#if ENABLE_WINMD_SUPPORT
            // HdmiDisplayInformation belongs to the app's view, so it only works on the UI thread
            UnityEngine.WSA.Application.InvokeOnUIThread(RequestOnUIThread, false);
#endif
        }

#if ENABLE_WINMD_SUPPORT
        private static void RequestOnUIThread()
        {
            try
            {
                var hdmi = HdmiDisplayInformation.GetForCurrentView();
                if (hdmi == null)
                {
                    YargLogger.LogInfo("No HDMI display information; keeping the current display mode.");
                    return;
                }

                var current = hdmi.GetCurrentDisplayMode();
                HdmiDisplayMode fastest = null;
                foreach (var mode in hdmi.GetSupportedDisplayModes())
                {
                    if (mode.ResolutionWidthInRawPixels != current.ResolutionWidthInRawPixels ||
                        mode.ResolutionHeightInRawPixels != current.ResolutionHeightInRawPixels ||
                        mode.ColorSpace != current.ColorSpace ||
                        mode.PixelEncoding != current.PixelEncoding ||
                        mode.BitsPerPixel != current.BitsPerPixel ||
                        mode.StereoEnabled)
                    {
                        continue;
                    }

                    if (mode.RefreshRate > (fastest ?? current).RefreshRate)
                    {
                        fastest = mode;
                    }
                }

                if (fastest == null)
                {
                    YargLogger.LogFormatInfo<uint, uint, double>(
                        "Display mode {0}x{1} at {2} Hz is already the fastest offered at this resolution.",
                        current.ResolutionWidthInRawPixels, current.ResolutionHeightInRawPixels, current.RefreshRate);
                    return;
                }

                double fromHz = current.RefreshRate;
                double toHz = fastest.RefreshRate;
                hdmi.RequestSetCurrentDisplayModeAsync(fastest).Completed = (operation, status) =>
                {
                    if (status == AsyncStatus.Completed && operation.GetResults())
                    {
                        YargLogger.LogFormatInfo<double, double>("Display refresh rate switched from {0} Hz to {1} Hz.",
                            fromHz, toHz);
                    }
                    else
                    {
                        YargLogger.LogFormatWarning<double, AsyncStatus>(
                            "The console refused a {0} Hz display mode ({1}); keeping the current one.", toHz, status);
                    }
                };
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to change the display refresh rate.");
            }
        }
#endif
    }
}
