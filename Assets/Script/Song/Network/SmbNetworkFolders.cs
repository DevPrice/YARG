using System;
using System.Collections.Generic;
using SMBLibrary;
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
        private static readonly HashSet<string> _offline = new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// Connects to <paramref name="folder"/> and opens it. Blocks on the network, for up to the connect
        /// timeout, so keep it off the main thread.
        /// </summary>
        public static NetworkFolderCheck Check(string folder)
        {
            return Check(SmbFileSystem.Shared, folder);
        }

        internal static NetworkFolderCheck Check(SmbFileSystem fileSystem, string folder)
        {
            if (!SmbPath.TryParse(folder, out _))
            {
                return new NetworkFolderCheck(folder, NetworkFolderStatus.InvalidPath, string.Empty);
            }

            NTStatus status;
            try
            {
                status = fileSystem.ProbeDirectory(folder);
            }
            catch (SmbConnectException e)
            {
                return new NetworkFolderCheck(folder, NetworkFolderStatus.Unreachable, e.Message);
            }
            catch (Exception e)
            {
                return new NetworkFolderCheck(folder, NetworkFolderStatus.Failed, e.Message);
            }

            var result = status switch
            {
                NTStatus.STATUS_SUCCESS          => NetworkFolderStatus.Online,
                NTStatus.STATUS_NOT_A_DIRECTORY  => NetworkFolderStatus.NotADirectory,
                NTStatus.STATUS_ACCESS_DENIED    => NetworkFolderStatus.AccessDenied,
                _ when SmbFileSystem.IsNotFound(status) => NetworkFolderStatus.NotFound,
                _                                => NetworkFolderStatus.Failed,
            };
            return new NetworkFolderCheck(folder, result, result == NetworkFolderStatus.Online ? string.Empty : status.ToString());
        }

        /// <summary>
        /// Checks each network folder, remembers which are offline for <see cref="IsOffline"/>, and removes those
        /// from <paramref name="directories"/>. Blocks on the network.
        /// </summary>
        /// <remarks>
        /// A full scan drops every cached song under a folder it can't read, so while any folder is offline the
        /// caller must run a quick scan, which keeps all cached entries regardless of the folder list.
        /// </remarks>
        /// <returns>The offline folders</returns>
        public static List<NetworkFolderCheck> ExcludeOffline(List<string> directories, IReadOnlyList<string> networkFolders)
        {
            return ExcludeOffline(SmbFileSystem.Shared, directories, networkFolders);
        }

        internal static List<NetworkFolderCheck> ExcludeOffline(SmbFileSystem fileSystem, List<string> directories,
            IReadOnlyList<string> networkFolders)
        {
            var offline = new List<NetworkFolderCheck>();
            foreach (string folder in networkFolders)
            {
                var check = Check(fileSystem, folder);
                if (check.Status == NetworkFolderStatus.Online)
                {
                    continue;
                }

                YargLogger.LogFormatWarning<string, NetworkFolderStatus, string>(
                    "Network song folder '{0}' is offline ({1}: {2}); keeping its cached songs",
                    folder, check.Status, check.Detail);
                offline.Add(check);
                directories.RemoveAll(directory => SameFolder(directory, folder));
            }

            lock (REGISTRATION_LOCK)
            {
                _offline.Clear();
                foreach (var check in offline)
                {
                    _offline.Add(check.Folder.TrimEnd('\\', '/'));
                }
            }
            return offline;
        }

        /// <summary>
        /// Whether the last scan found <paramref name="folder"/> unreachable.
        /// </summary>
        public static bool IsOffline(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            lock (REGISTRATION_LOCK)
            {
                return _offline.Contains(folder.TrimEnd('\\', '/'));
            }
        }

        private static bool SameFolder(string lhs, string rhs)
        {
            return lhs != null && string.Equals(lhs.TrimEnd('\\', '/'), rhs.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum NetworkFolderStatus
    {
        Online,
        InvalidPath,
        Unreachable,
        NotFound,
        AccessDenied,
        NotADirectory,
        Failed,
    }

    public readonly struct NetworkFolderCheck
    {
        public readonly string Folder;
        public readonly NetworkFolderStatus Status;

        /// <summary>
        /// The SMB status or error behind a failed check, for display; empty when online.
        /// </summary>
        public readonly string Detail;

        public NetworkFolderCheck(string folder, NetworkFolderStatus status, string detail)
        {
            Folder = folder;
            Status = status;
            Detail = detail;
        }
    }
}
