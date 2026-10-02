using System;
using System.Collections.Generic;
using System.IO;
using SMBLibrary;
using SMBLibrary.Client;
using YARG.Core.IO;
using FileAttributes = SMBLibrary.FileAttributes;

namespace YARG.Song.Network
{
    /// <summary>
    /// Read-only access to \\server\share paths through an in-process SMB2 client (SMBLibrary), so song folders
    /// on a NAS work without the OS mounting the share. Logs in as guest. Names match case-insensitively, as
    /// the server decides.
    /// </summary>
    public sealed class SmbFileSystem : IYARGFileSystem, IDisposable
    {
        public const int MAX_REQUESTS_PER_SERVER = 8;

        public static readonly SmbFileSystem Shared = new();

        private static readonly DateTime FILETIME_ZERO = DateTime.FromFileTimeUtc(0);

        private readonly Func<string, SmbServer> _serverFactory;
        private readonly Dictionary<string, SmbServer> _servers = new(StringComparer.OrdinalIgnoreCase);

        public SmbFileSystem()
            : this(host => new SmbServer(host, () => new Smb2Session(host), MAX_REQUESTS_PER_SERVER))
        {
        }

        internal SmbFileSystem(Func<string, SmbServer> serverFactory)
        {
            _serverFactory = serverFactory;
        }

        public bool TryStat(string path, out YARGFileStat stat)
        {
            stat = default;
            if (!SmbPath.TryParse(path, out var smb))
            {
                return false;
            }

            var result = default(YARGFileStat);
            var status = GetServer(smb.Server).Run(null, smb.Share, (store, _) =>
            {
                var openStatus = Open(store, smb.Path, OpenKind.Attributes, out object handle);
                if (openStatus != NTStatus.STATUS_SUCCESS)
                {
                    return openStatus;
                }

                try
                {
                    var infoStatus = store.GetFileInformation(out var info, handle, FileInformationClass.FileNetworkOpenInformation);
                    if (infoStatus == NTStatus.STATUS_SUCCESS)
                    {
                        var open = (FileNetworkOpenInformation) info;
                        bool isDirectory = (open.FileAttributes & FileAttributes.Directory) != 0;
                        result = new YARGFileStat(isDirectory, isDirectory ? 0 : open.EndOfFile,
                            ToUtc(open.LastWriteTime), ToUtc(open.CreationTime));
                    }
                    return infoStatus;
                }
                finally
                {
                    store.CloseFile(handle);
                }
            }, out _);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                stat = result;
                return true;
            }

            // FileInfo.Exists also reports false for paths it may not read.
            if (IsNotFound(status) || status == NTStatus.STATUS_ACCESS_DENIED)
            {
                return false;
            }
            throw new IOException($"Could not stat '{path}': {status}");
        }

        /// <exception cref="DirectoryNotFoundException"/>
        public IEnumerable<YARGFileSystemEntry> Enumerate(string directory)
        {
            if (!SmbPath.TryParse(directory, out var smb))
            {
                throw new DirectoryNotFoundException($"Not an SMB share path: '{directory}'");
            }

            var entries = new List<YARGFileSystemEntry>();
            var status = GetServer(smb.Server).Run(null, smb.Share, (store, _) =>
            {
                entries.Clear();
                var openStatus = Open(store, smb.Path, OpenKind.Directory, out object handle);
                if (openStatus != NTStatus.STATUS_SUCCESS)
                {
                    return openStatus;
                }

                try
                {
                    var queryStatus = store.QueryDirectory(out var listing, handle, "*", FileInformationClass.FileDirectoryInformation);
                    if (queryStatus != NTStatus.STATUS_NO_MORE_FILES && queryStatus != NTStatus.STATUS_NO_SUCH_FILE &&
                        queryStatus != NTStatus.STATUS_SUCCESS)
                    {
                        return queryStatus;
                    }

                    if (listing != null)
                    {
                        foreach (var item in listing)
                        {
                            if (item is FileDirectoryInformation info && info.FileName != "." && info.FileName != "..")
                            {
                                entries.Add(ToEntry(directory, info));
                            }
                        }
                    }
                    return NTStatus.STATUS_SUCCESS;
                }
                finally
                {
                    store.CloseFile(handle);
                }
            }, out _);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                return entries;
            }
            if (IsNotFound(status))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path '{directory}'");
            }
            throw ToException(status, directory);
        }

        public Stream OpenRead(string path, int bufferSize = YARGFileSystem.DEFAULT_BUFFER_SIZE)
        {
            if (!SmbPath.TryParse(path, out var smb))
            {
                throw new FileNotFoundException($"Not an SMB share path: '{path}'", path);
            }
            return SmbReadStream.Open(GetServer(smb.Server), smb, path, bufferSize);
        }

        public bool FileExists(string path)
        {
            return TryStat(path, out var stat) && !stat.IsDirectory;
        }

        public bool DirectoryExists(string path)
        {
            return TryStat(path, out var stat) && stat.IsDirectory;
        }

        /// <summary>
        /// Drops every connection. Later calls reconnect.
        /// </summary>
        public void Dispose()
        {
            lock (_servers)
            {
                foreach (var server in _servers.Values)
                {
                    server.Dispose();
                }
                _servers.Clear();
            }
        }

        internal enum OpenKind
        {
            Attributes,
            File,
            Directory,
        }

        /// <summary>
        /// The only CreateFile call in the backend: FILE_OPEN with read-only access, so nothing here can create,
        /// write, rename or delete.
        /// </summary>
        internal static NTStatus Open(ISMBFileStore store, string path, OpenKind kind, out object handle)
        {
            AccessMask access;
            CreateOptions options;
            switch (kind)
            {
                case OpenKind.Attributes:
                    access = (AccessMask) (FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.SYNCHRONIZE);
                    options = 0;
                    break;
                case OpenKind.File:
                    access = AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE;
                    options = CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT;
                    break;
                case OpenKind.Directory:
                    access = AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE;
                    options = CreateOptions.FILE_DIRECTORY_FILE;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return store.CreateFile(out handle, out _, path, access, 0, ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN, options, null);
        }

        internal static bool IsNotFound(NTStatus status)
        {
            switch (status)
            {
                case NTStatus.STATUS_OBJECT_NAME_NOT_FOUND:
                case NTStatus.STATUS_OBJECT_PATH_NOT_FOUND:
                case NTStatus.STATUS_OBJECT_NAME_INVALID:
                case NTStatus.STATUS_OBJECT_PATH_SYNTAX_BAD:
                case NTStatus.STATUS_NO_SUCH_FILE:
                case NTStatus.STATUS_NOT_A_DIRECTORY:
                case NTStatus.STATUS_BAD_NETWORK_NAME:
                case NTStatus.STATUS_DELETE_PENDING:
                    return true;
                default:
                    return false;
            }
        }

        internal static Exception ToException(NTStatus status, string path)
        {
            if (status == NTStatus.STATUS_ACCESS_DENIED || status == NTStatus.STATUS_FILE_IS_A_DIRECTORY)
            {
                return new UnauthorizedAccessException($"Access to the path '{path}' is denied ({status})");
            }
            return new IOException($"SMB error on '{path}': {status}");
        }

        /// <summary>
        /// SMBLibrary decodes FILETIMEs as UTC; this pins the kind so cache keys built with ToLocalTime() stay stable.
        /// </summary>
        internal static DateTime ToUtc(DateTime? time)
        {
            if (!time.HasValue)
            {
                return FILETIME_ZERO;
            }

            var value = time.Value;
            return value.Kind switch
            {
                DateTimeKind.Utc   => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        private static YARGFileSystemEntry ToEntry(string directory, FileDirectoryInformation info)
        {
            bool isDirectory = (info.FileAttributes & FileAttributes.Directory) != 0;
            var stat = new YARGFileStat(isDirectory, isDirectory ? 0 : info.EndOfFile,
                ToUtc(info.LastWriteTime), ToUtc(info.CreationTime));
            return new YARGFileSystemEntry(info.FileName, Path.Combine(directory, info.FileName), in stat);
        }

        private SmbServer GetServer(string host)
        {
            lock (_servers)
            {
                if (!_servers.TryGetValue(host, out var server))
                {
                    server = _serverFactory(host);
                    _servers.Add(host, server);
                }
                return server;
            }
        }
    }
}
