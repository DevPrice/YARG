using System;
using System.IO;
using SMBLibrary;
using SMBLibrary.Client;

namespace YARG.Song.Network
{
    /// <summary>
    /// A seekable, read-only stream over one SMB file handle. Reads fill the request unless they reach the end
    /// of the file, and small reads are served from a read-ahead buffer, since song parsers issue many tiny
    /// reads that would otherwise each cost a round trip. Members lock, so callers may share the stream.
    /// </summary>
    public sealed class SmbReadStream : Stream
    {
        public const int MIN_READ_AHEAD = 64 * 1024;
        public const int MAX_READ_AHEAD = 1024 * 1024;

        // Container formats alternate between a table and the data it describes (STFS reads a hash block before
        // each run of data blocks), which would make a single read-ahead buffer refetch on every switch.
        private const int WINDOW_COUNT = 4;

        private readonly SmbServer _server;
        private readonly SmbSession _session;
        private readonly string _share;
        private readonly string _path;
        private readonly string _displayPath;
        private readonly long _length;
        private readonly object _lock = new();

        private object _handle;
        private int _handleGeneration;

        private readonly int _windowSize;
        private readonly Window[] _windows = new Window[WINDOW_COUNT];
        private long _useClock;

        private long _position;
        private bool _disposed;

        private SmbReadStream(SmbServer server, SmbSession session, in SmbPath path, string displayPath,
            object handle, int generation, long length, int bufferSize)
        {
            _server = server;
            _session = session;
            _share = path.Share;
            _path = path.Path;
            _displayPath = displayPath;
            _handle = handle;
            _handleGeneration = generation;
            _length = length;
            _windowSize = Math.Clamp(bufferSize, MIN_READ_AHEAD, MAX_READ_AHEAD);
        }

        private sealed class Window
        {
            public readonly byte[] Data;
            public long Start;
            public int Count;
            public long LastUse;

            public Window(int size)
            {
                Data = new byte[size];
            }
        }

        /// <exception cref="FileNotFoundException"/>
        /// <exception cref="UnauthorizedAccessException">Access denied, or the path is a directory</exception>
        internal static SmbReadStream Open(SmbServer server, in SmbPath path, string displayPath, int bufferSize)
        {
            object handle = null;
            int generation = 0;
            long length = 0;
            string relative = path.Path;
            var status = server.Run(null, path.Share, (store, gen) =>
            {
                var openStatus = SmbFileSystem.Open(store, relative, SmbFileSystem.OpenKind.File, out handle);
                if (openStatus != NTStatus.STATUS_SUCCESS)
                {
                    return openStatus;
                }

                generation = gen;
                var infoStatus = store.GetFileInformation(out var info, handle, FileInformationClass.FileStandardInformation);
                if (infoStatus == NTStatus.STATUS_SUCCESS)
                {
                    length = ((FileStandardInformation) info).EndOfFile;
                }
                else
                {
                    store.CloseFile(handle);
                    handle = null;
                }
                return infoStatus;
            }, out var session);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                return new SmbReadStream(server, session, path, displayPath, handle, generation, length, bufferSize);
            }
            if (SmbFileSystem.IsNotFound(status))
            {
                throw new FileNotFoundException($"Could not find file '{displayPath}'", displayPath);
            }
            throw SmbFileSystem.ToException(status, displayPath);
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return _length;
            }
        }

        public override long Position
        {
            get
            {
                lock (_lock)
                {
                    ThrowIfDisposed();
                    return _position;
                }
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (_lock)
                {
                    ThrowIfDisposed();
                    _position = value;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0 || count < 0 || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            lock (_lock)
            {
                ThrowIfDisposed();
                if (_position >= _length)
                {
                    return 0;
                }

                count = (int) Math.Min(count, _length - _position);
                int total = 0;
                while (total < count)
                {
                    var window = FindWindow(_position);
                    if (window != null)
                    {
                        int windowOffset = (int) (_position - window.Start);
                        int copy = Math.Min(count - total, window.Count - windowOffset);
                        Buffer.BlockCopy(window.Data, windowOffset, buffer, offset + total, copy);
                        total += copy;
                        _position += copy;
                        continue;
                    }

                    int remaining = count - total;
                    if (remaining >= _windowSize)
                    {
                        int read = ReadRemote(_position, buffer, offset + total, remaining);
                        total += read;
                        _position += read;
                        if (read < remaining)
                        {
                            break;
                        }
                        continue;
                    }

                    window = ClaimWindow();
                    window.Start = _position;
                    window.Count = ReadRemote(_position, window.Data, 0, (int) Math.Min(_windowSize, _length - _position));
                    if (window.Count == 0)
                    {
                        break;
                    }
                }
                return total;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                long target = origin switch
                {
                    SeekOrigin.Begin   => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End     => _length + offset,
                    _                  => throw new ArgumentOutOfRangeException(nameof(origin)),
                };
                if (target < 0)
                {
                    throw new IOException("An attempt was made to move the position before the beginning of the stream.");
                }
                _position = target;
                return target;
            }
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }

            if (disposing)
            {
                CloseHandle();
            }
            base.Dispose(disposing);
        }

        private Window FindWindow(long position)
        {
            foreach (var window in _windows)
            {
                if (window != null && position >= window.Start && position - window.Start < window.Count)
                {
                    window.LastUse = ++_useClock;
                    return window;
                }
            }
            return null;
        }

        /// <summary>
        /// An unallocated slot while there is one, else the least recently used window.
        /// </summary>
        private Window ClaimWindow()
        {
            int victim = 0;
            for (int i = 0; i < _windows.Length; i++)
            {
                if (_windows[i] == null)
                {
                    victim = i;
                    _windows[i] = new Window(_windowSize);
                    break;
                }
                if (_windows[i].LastUse < _windows[victim].LastUse)
                {
                    victim = i;
                }
            }

            var window = _windows[victim];
            window.Count = 0;
            window.LastUse = ++_useClock;
            return window;
        }

        /// <returns>Bytes read; fewer than <paramref name="count"/> only at the end of the file</returns>
        private int ReadRemote(long position, byte[] destination, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                byte[] data = null;
                long readAt = position + total;
                int want = count - total;
                var status = _server.Run(_session, _share, (store, generation) =>
                {
                    var reopenStatus = EnsureHandle(store, generation);
                    if (reopenStatus != NTStatus.STATUS_SUCCESS)
                    {
                        return reopenStatus;
                    }
                    int chunk = (int) Math.Min(want, Math.Max(store.MaxReadSize, 4096u));
                    return store.ReadFile(out data, _handle, readAt, chunk);
                }, out _);

                if (status == NTStatus.STATUS_END_OF_FILE)
                {
                    break;
                }
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Could not read '{_displayPath}' at offset {readAt}: {status}");
                }
                if (data == null || data.Length == 0)
                {
                    break;
                }

                int copy = Math.Min(data.Length, want);
                Buffer.BlockCopy(data, 0, destination, offset + total, copy);
                total += copy;
            }
            return total;
        }

        private NTStatus EnsureHandle(ISMBFileStore store, int generation)
        {
            if (_handle != null && _handleGeneration == generation)
            {
                return NTStatus.STATUS_SUCCESS;
            }

            _handle = null;
            var status = SmbFileSystem.Open(store, _path, SmbFileSystem.OpenKind.File, out object handle);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                _handle = handle;
                _handleGeneration = generation;
            }
            return status;
        }

        private void CloseHandle()
        {
            if (_handle == null)
            {
                return;
            }

            lock (_session.Lock)
            {
                try
                {
                    if (_session.IsConnected && _session.Generation == _handleGeneration &&
                        _session.TryGetStore(_share, out var store) == NTStatus.STATUS_SUCCESS)
                    {
                        store.CloseFile(_handle);
                    }
                }
                catch (Exception)
                {
                    // The connection is gone, and the server released the handle with it.
                }
                _handle = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SmbReadStream));
            }
        }
    }
}
