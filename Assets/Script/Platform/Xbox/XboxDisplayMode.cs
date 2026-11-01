#if ENABLE_WINMD_SUPPORT
using System;
using System.Collections.Generic;
using System.Text;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Graphics.Display.Core;
using YARG.Core.Logging;
#endif

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Chooses the console's HDMI output mode by <see cref="DisplayModePreference"/>. A UWP app on Xbox gets the
    /// console's 60 Hz mode unless it asks for another, even when the dashboard runs at 120 Hz. The choice is
    /// made again on resume and when the TV's modes change, since the console may put its own mode back.
    /// </summary>
    public static class XboxDisplayMode
    {
        public static void Apply(DisplayModePreference preference)
        {
#if ENABLE_WINMD_SUPPORT
            _preference = preference;
            // HdmiDisplayInformation belongs to the app's view, so it only works on the UI thread
            UnityEngine.WSA.Application.InvokeOnUIThread(ApplyOnUIThread, false);
#endif
        }

#if ENABLE_WINMD_SUPPORT
        private static DisplayModePreference _preference;
        private static HdmiDisplayInformation _hdmi;

        // The console's own mode, which caps the resolution we pick. Only valid while the current mode isn't one
        // we requested; it's captured again whenever the console has changed the mode since.
        private static HdmiDisplayMode _systemMode;
        private static HdmiDisplayMode _requestedMode;

        private static void ApplyOnUIThread()
        {
            try
            {
                if (_hdmi == null)
                {
                    _hdmi = HdmiDisplayInformation.GetForCurrentView();
                    if (_hdmi == null)
                    {
                        YargLogger.LogInfo("No HDMI display information; keeping the console's display mode.");
                        return;
                    }

                    _hdmi.DisplayModesChanged += (sender, args) =>
                        UnityEngine.WSA.Application.InvokeOnUIThread(ApplyOnUIThread, false);
                    CoreApplication.Resuming += (sender, args) =>
                        UnityEngine.WSA.Application.InvokeOnUIThread(ApplyOnUIThread, false);
                }

                var current = _hdmi.GetCurrentDisplayMode();
                var modes = _hdmi.GetSupportedDisplayModes();
                if (_requestedMode == null || !current.IsEqual(_requestedMode))
                {
                    _systemMode = current;
                    _requestedMode = null;
                    LogModes(modes, current);
                }

                if (_preference == DisplayModePreference.System)
                {
                    if (_requestedMode != null)
                    {
                        _requestedMode = null;
                        _ = _hdmi.SetDefaultDisplayModeAsync();
                        YargLogger.LogFormatInfo<string>("Returning to the console's display mode, {0}.",
                            Describe(_systemMode));
                    }
                    return;
                }

                var chosen = Choose(modes, _systemMode, _preference);
                if (chosen == null || chosen.IsEqual(current))
                {
                    YargLogger.LogFormatInfo<string, DisplayModePreference>("Keeping display mode {0} ({1}).",
                        Describe(current), _preference);
                    return;
                }

                Request(chosen, current);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to choose the display mode.");
            }
        }

        private static void Request(HdmiDisplayMode mode, HdmiDisplayMode from)
        {
            string fromText = Describe(from);
            string toText = Describe(mode);
            _requestedMode = mode;
            _hdmi.RequestSetCurrentDisplayModeAsync(mode).Completed = (operation, status) =>
            {
                if (status == AsyncStatus.Completed && operation.GetResults())
                {
                    YargLogger.LogFormatInfo<string, string>("Display mode switched from {0} to {1}.", fromText, toText);
                }
                else
                {
                    UnityEngine.WSA.Application.InvokeOnUIThread(() => _requestedMode = null, false);
                    YargLogger.LogFormatWarning<string, AsyncStatus>(
                        "The console refused display mode {0} ({1}); keeping the current one.", toText, status);
                }
            };
        }

        /// <summary>
        /// The best SDR mode no larger than the console's own resolution: for <see cref="DisplayModePreference.RefreshRate"/>
        /// the fastest refresh rate, then the most pixels; for <see cref="DisplayModePreference.Resolution"/> the
        /// fastest refresh rate at the console's resolution. Ties go to the console's own color format.
        /// </summary>
        private static HdmiDisplayMode Choose(IReadOnlyList<HdmiDisplayMode> modes, HdmiDisplayMode system,
            DisplayModePreference preference)
        {
            HdmiDisplayMode best = null;
            foreach (var mode in modes)
            {
                // YARG renders SDR, and an HDR mode would make the console tone-map it
                if (mode.StereoEnabled || mode.ColorSpace == HdmiDisplayColorSpace.BT2020)
                {
                    continue;
                }

                uint width = mode.ResolutionWidthInRawPixels;
                uint height = mode.ResolutionHeightInRawPixels;
                if (width > system.ResolutionWidthInRawPixels || height > system.ResolutionHeightInRawPixels)
                {
                    continue;
                }

                if (preference == DisplayModePreference.Resolution &&
                    (width != system.ResolutionWidthInRawPixels || height != system.ResolutionHeightInRawPixels))
                {
                    continue;
                }

                if (best == null || IsBetter(mode, best, system))
                {
                    best = mode;
                }
            }

            return best;
        }

        private static bool IsBetter(HdmiDisplayMode a, HdmiDisplayMode b, HdmiDisplayMode system)
        {
            // Whole hertz, so 1440p at 119.88 Hz still beats 1080p at 120 Hz
            long hzA = (long) Math.Round(a.RefreshRate);
            long hzB = (long) Math.Round(b.RefreshRate);
            if (hzA != hzB)
            {
                return hzA > hzB;
            }

            long pixelsA = (long) a.ResolutionWidthInRawPixels * a.ResolutionHeightInRawPixels;
            long pixelsB = (long) b.ResolutionWidthInRawPixels * b.ResolutionHeightInRawPixels;
            if (pixelsA != pixelsB)
            {
                return pixelsA > pixelsB;
            }

            int matchA = FormatMatches(a, system);
            int matchB = FormatMatches(b, system);
            if (matchA != matchB)
            {
                return matchA > matchB;
            }

            return a.RefreshRate > b.RefreshRate;
        }

        private static int FormatMatches(HdmiDisplayMode mode, HdmiDisplayMode system)
        {
            int matches = 0;
            if (mode.ColorSpace == system.ColorSpace) matches++;
            if (mode.PixelEncoding == system.PixelEncoding) matches++;
            if (mode.BitsPerPixel == system.BitsPerPixel) matches++;
            return matches;
        }

        private static void LogModes(IReadOnlyList<HdmiDisplayMode> modes, HdmiDisplayMode current)
        {
            var builder = new StringBuilder();
            builder.Append("HDMI modes (* = current):");
            foreach (var mode in modes)
            {
                builder.Append("\n  ").Append(mode.IsEqual(current) ? "* " : "  ").Append(Describe(mode));
            }
            YargLogger.LogInfo(builder.ToString());
        }

        private static string Describe(HdmiDisplayMode mode) =>
            $"{mode.ResolutionWidthInRawPixels}x{mode.ResolutionHeightInRawPixels} @ {mode.RefreshRate:0.###} Hz " +
            $"{mode.ColorSpace} {mode.PixelEncoding} {mode.BitsPerPixel} bpp";
#endif
    }
}
