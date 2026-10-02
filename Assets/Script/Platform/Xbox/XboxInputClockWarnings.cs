using System;
using UnityEngine.InputSystem;
using YARG.Core.Logging;

namespace YARG.Platform.Xbox
{
    /// <summary>
    /// Rate-limited stand-ins for InputManager's "input event is in the future" errors. On UWP, event timestamps
    /// can run ahead of InputState.currentTime routinely, and an error per event would flood the log.
    /// Times are input-system times in seconds; nothing here changes how the events are processed.
    /// </summary>
    public static class XboxInputClockWarnings
    {
        private const double REPORT_INTERVAL_SECONDS = 10;

        private static LeadTally _futureEvents;
        private static LeadTally _futureUpdates;

        public static void ReportFutureEvent(double currentTime, double eventTime, InputDevice device)
        {
            if (_futureEvents.Add(currentTime, eventTime - currentTime, out int count, out double maxLeadMs))
            {
                YargLogger.LogFormatWarning<int, double, string, double>(
                    "{0} input event(s) were ahead of the input clock, by up to {1} ms (latest device: {2}). Reported at most every {3} s.",
                    count, maxLeadMs, device?.displayName, REPORT_INTERVAL_SECONDS);
            }
        }

        public static void ReportFutureUpdate(double updateTime, double latestInputTime)
        {
            if (_futureUpdates.Add(updateTime, latestInputTime - updateTime, out int count, out double maxLeadMs))
            {
                YargLogger.LogFormatWarning<int, double, double>(
                    "{0} input update(s) ended before their latest input event, by up to {1} ms. Reported at most every {2} s.",
                    count, maxLeadMs, REPORT_INTERVAL_SECONDS);
            }
        }

        private struct LeadTally
        {
            private double _nextReportTime;
            private int _count;
            private double _maxLead;

            public bool Add(double now, double lead, out int count, out double maxLeadMs)
            {
                _count++;
                _maxLead = Math.Max(_maxLead, lead);
                count = _count;
                maxLeadMs = Math.Round(_maxLead * 1000, 3);

                if (now < _nextReportTime)
                {
                    return false;
                }

                _nextReportTime = now + REPORT_INTERVAL_SECONDS;
                _count = 0;
                _maxLead = 0;
                return true;
            }
        }
    }
}
