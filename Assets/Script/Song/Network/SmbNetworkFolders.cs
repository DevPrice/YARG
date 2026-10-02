using System;
using System.Collections.Generic;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Song.Network
{
    /// <summary>
    /// Routes the user's network song folders to <see cref="SmbFileSystem.Shared"/>. Only these roots use the
    /// in-process client; any other \\server path, such as one in the regular song folders, still goes through
    /// the OS.
    /// </summary>
    public static class SmbNetworkFolders
    {
        private static readonly object REGISTRATION_LOCK = new();
        private static readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers every valid \\server\share[\path] folder and unregisters roots that are no longer listed.
        /// Connects lazily, on first access.
        /// </summary>
        /// <returns>The folders that were registered, for the scan's folder list</returns>
        public static List<string> Register(IEnumerable<string> folders)
        {
            var valid = new List<string>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        continue;
                    }

                    if (!SmbPath.TryParse(folder, out var path))
                    {
                        YargLogger.LogFormatWarning("Ignoring network song folder '{0}': expected \\\\server\\share\\path", folder);
                        continue;
                    }

                    // Canonical spelling, so a saved "\\\\server\share" or "//server/share" still matches what the
                    // scanner walks; a mismatched root falls through to the OS file system and fails.
                    string root = path.ToString();
                    if (roots.Add(root))
                    {
                        valid.Add(root);
                    }
                }
            }

            lock (REGISTRATION_LOCK)
            {
                foreach (string stale in _registered)
                {
                    if (!roots.Contains(stale))
                    {
                        YARGFileSystem.Unregister(stale);
                    }
                }
                _registered.Clear();

                foreach (string root in valid)
                {
                    YARGFileSystem.Register(root, SmbFileSystem.Shared);
                    _registered.Add(root);
                }
            }
            return valid;
        }
    }
}
