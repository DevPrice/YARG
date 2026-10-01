using System;
using System.Collections.Generic;

namespace YARG.Song.Network
{
    /// <summary>
    /// A UNC path split into server, share and the share-relative path that SMB requests take.
    /// </summary>
    public readonly struct SmbPath
    {
        public readonly string Server;
        public readonly string Share;

        /// <summary>
        /// Backslash-separated, with no leading or trailing separator; empty for the share root.
        /// </summary>
        public readonly string Path;

        public SmbPath(string server, string share, string path)
        {
            Server = server;
            Share = share;
            Path = path;
        }

        /// <summary>
        /// Accepts \\server\share[\path] with either separator, collapsing repeated separators and "." / ".."
        /// segments. Rejects device paths (\\?\, \\.\), a bare \\server, and ".." above the share.
        /// </summary>
        public static bool TryParse(string unc, out SmbPath result)
        {
            result = default;
            if (unc == null || unc.Length < 3 || !IsSeparator(unc[0]) || !IsSeparator(unc[1]))
            {
                return false;
            }

            var segments = new List<string>();
            foreach (string segment in unc.Substring(2).Split('\\', '/'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                if (segments.Count == 0)
                {
                    if (segment == "." || segment == "?")
                    {
                        return false;
                    }
                }
                else if (segment == ".")
                {
                    continue;
                }
                else if (segment == "..")
                {
                    if (segments.Count <= 2)
                    {
                        return false;
                    }
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }

            if (segments.Count < 2)
            {
                return false;
            }

            result = new SmbPath(segments[0], segments[1], string.Join("\\", segments.GetRange(2, segments.Count - 2)));
            return true;
        }

        public override string ToString()
        {
            return Path.Length == 0
                ? $@"\\{Server}\{Share}"
                : $@"\\{Server}\{Share}\{Path}";
        }

        private static bool IsSeparator(char c)
        {
            return c == '\\' || c == '/';
        }
    }
}
