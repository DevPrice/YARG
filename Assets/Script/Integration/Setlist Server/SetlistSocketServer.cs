using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using YARG.Core.Logging;
using static YARG.Integration.SetlistProtocol;

namespace YARG.Integration
{
    /// <summary>
    /// One run of the setlist socket: a loopback listener and the background thread that serves it. Stopping
    /// abandons the instance, and a restart creates a new one, so an exiting thread never shares state with its
    /// successor.
    /// </summary>
    /// <remarks>
    /// Steady state allocates nothing once clients are connected; only accepting a connection and logging do.
    /// </remarks>
    internal sealed class SetlistSocketServer
    {
        private const int MAX_CLIENTS        = 4;
        private const int MAX_PENDING        = 4;
        private const int ACCEPTS_PER_SECOND = 4;
        private const int SEND_BUFFER_SIZE   = 256 * 1024;
        private const int SEND_TIMEOUT_MS    = 1000;
        private const int WAKE_INTERVAL_MS   = 10;

        // Everything sent to a client that it hasn't proven it read. Kept under SEND_BUFFER_SIZE so a send always
        // fits in the kernel buffer.
        private const int MAX_UNACKED_BYTES = 192 * 1024;

        // Tokens of the most recent frames sent to a client, which its acks are matched against
        private const int RECENT_FRAMES = 32;

        private const int QUEUE_SIZE            = 64;
        private const int MAX_QUEUED_PER_CLIENT = 4;

        private static readonly long HandshakeTimeout = Stopwatch.Frequency * 2;
        private static readonly long AckTimeout       = Stopwatch.Frequency * 10;
        private static readonly long LingerTimeout    = Stopwatch.Frequency;

        private enum ClientState
        {
            Free,
            Handshake,
            Ready,

            /// <summary>
            /// Sent a non-ok hello and shut down sending; waiting for the client to close, so the client reads the
            /// status instead of a reset.
            /// </summary>
            Closing,
        }

        private sealed class Client
        {
            public Socket      Socket;
            public ClientState State;
            public long        Deadline;
            public ushort      Version;

            /// <summary>
            /// Changes on every accept, so a result for a dropped client never reaches the next one in this slot.
            /// </summary>
            public uint Generation;

            public readonly byte[] Received = new byte[FRAME_HEADER_SIZE + MAX_CLIENT_FRAME];
            public int ReceivedCount;

            public uint SentRevision;
            public long SentAt;

            // Bytes sent since the server hello, and how far the client has proven it read
            public long SentBytes;
            public long AckedBytes;
            public long SnapshotEnd;

            public readonly uint[] RecentTokens = new uint[RECENT_FRAMES];
            public readonly long[] RecentEnds   = new long[RECENT_FRAMES];
            public int RecentNext;

            public int QueuedCommands;

            public bool AwaitingAck => AckedBytes < SnapshotEnd;
            public long Unacked     => SentBytes - AckedBytes;
        }

        public struct QueuedCommand
        {
            public int     Client;
            public uint    Generation;
            public Command Command;
        }

        public struct QueuedResult
        {
            public int          Client;
            public uint         Generation;
            public uint         RequestId;
            public ResultStatus Status;
            public uint         EntryId;
            public uint         Revision;
        }

        public int Port { get; }

        /// <summary>
        /// Set when the socket thread stopped on an unexpected error, leaving nothing listening.
        /// </summary>
        public bool Faulted => _faulted;

        private readonly Socket         _listener;
        private readonly Thread         _thread;
        private readonly AutoResetEvent _wake = new(false);
        private volatile bool           _stopping;
        private volatile bool           _faulted;

        private readonly Client[] _clients = new Client[MAX_CLIENTS + MAX_PENDING];
        private int _serviceStart;

        private readonly ulong  _sessionNonce;
        private readonly byte[] _helloBuffer  = new byte[SERVER_HELLO_SIZE];
        private readonly byte[] _resultBuffer = new byte[RESULT_FRAME_SIZE];
        private readonly byte[] _snapshotV1   = new byte[MAX_SNAPSHOT_BYTES];
        private readonly byte[] _snapshotV2   = new byte[MAX_SNAPSHOT_BYTES];
        private int  _snapshotV1Length;
        private int  _snapshotV2Length;
        private uint _snapshotRevision;
        private uint _snapshotToken;

        private readonly RandomNumberGenerator _random      = RandomNumberGenerator.Create();
        private readonly byte[]                _randomBytes = new byte[256];
        private int _randomOffset = int.MaxValue;

        private long _acceptWindowStart;
        private int  _acceptsInWindow;
        private long _lastLogAt;

        // A triple buffer: the main thread fills _writing, the socket thread encodes _reading, and _latest is
        // swapped between them under the lock, so neither side ever waits for the other's work.
        private readonly object _swapLock = new();
        private SetlistSnapshot _writing = new();
        private SetlistSnapshot _latest  = new();
        private SetlistSnapshot _reading = new();
        private bool _hasLatest;
        private uint _publishedRevision;

        // Fixed rings between the threads: commands to the main thread, results back
        private readonly object          _queueLock = new();
        private readonly QueuedCommand[] _commands  = new QueuedCommand[QUEUE_SIZE];
        private readonly QueuedResult[]  _results   = new QueuedResult[QUEUE_SIZE];
        private int _commandHead;
        private int _commandCount;
        private int _resultHead;
        private int _resultCount;

        /// <summary>
        /// Binds 127.0.0.1:<paramref name="port"/> on the calling thread, so a port in use throws here.
        /// </summary>
        /// <exception cref="SocketException">The port couldn't be bound.</exception>
        public static SetlistSocketServer Start(int port)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
                listener.Listen(MAX_PENDING);
            }
            catch
            {
                listener.Dispose();
                throw;
            }

            var server = new SetlistSocketServer(listener, port);
            server._thread.Start();
            return server;
        }

        private SetlistSocketServer(Socket listener, int port)
        {
            _listener = listener;
            Port = port;
            _sessionNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);

            for (int i = 0; i < _clients.Length; i++)
            {
                _clients[i] = new Client();
            }

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "YARG Setlist Socket",
            };
        }

        /// <summary>
        /// The snapshot to fill before calling <see cref="Publish"/>. Main thread only.
        /// </summary>
        public SetlistSnapshot PendingSnapshot => _writing;

        /// <summary>
        /// The revision of the last published snapshot. Main thread only.
        /// </summary>
        public uint PublishedRevision => _publishedRevision;

        /// <summary>
        /// Hands <see cref="PendingSnapshot"/> to the socket thread. Main thread only.
        /// </summary>
        public void Publish()
        {
            _writing.Revision = ++_publishedRevision;
            lock (_swapLock)
            {
                (_writing, _latest) = (_latest, _writing);
                _hasLatest = true;
            }

            _wake.Set();
        }

        public bool TryTakeCommand(out QueuedCommand command)
        {
            lock (_queueLock)
            {
                if (_commandCount == 0)
                {
                    command = default;
                    return false;
                }

                command = _commands[_commandHead];
                _commandHead = (_commandHead + 1) % QUEUE_SIZE;
                _commandCount--;
                return true;
            }
        }

        /// <summary>
        /// How many results <see cref="PostResult"/> can take now. Take no more commands than this.
        /// </summary>
        public int FreeResultSlots
        {
            get
            {
                lock (_queueLock)
                {
                    return QUEUE_SIZE - _resultCount;
                }
            }
        }

        /// <summary>
        /// Queues a result for the socket thread; call <see cref="SendResults"/> once a batch is posted.
        /// </summary>
        public void PostResult(in QueuedResult result)
        {
            lock (_queueLock)
            {
                if (_resultCount == QUEUE_SIZE)
                {
                    return;
                }

                _results[(_resultHead + _resultCount) % QUEUE_SIZE] = result;
                _resultCount++;
            }
        }

        public void SendResults() => _wake.Set();

        /// <summary>
        /// Returns at once; the socket thread closes its clients and exits within a few milliseconds.
        /// </summary>
        public void Stop()
        {
            _stopping = true;
            _wake.Set();

            // Frees the port now, so a restart can bind it straight away
            _listener.Close();
        }

        private void Run()
        {
            try
            {
                while (!_stopping)
                {
                    _wake.WaitOne(WAKE_INTERVAL_MS);
                    if (_stopping)
                    {
                        break;
                    }

                    long now = Stopwatch.GetTimestamp();
                    AcceptClients(now);
                    ServiceClients(now);

                    if (TakeLatestSnapshot())
                    {
                        // Encode both, so a client of either version that connects later has one to start with
                        _snapshotToken = NextToken();
                        _snapshotV1Length = WriteSnapshot(_snapshotV1, _reading, 1, _snapshotToken);
                        _snapshotV2Length = WriteSnapshot(_snapshotV2, _reading, 2, _snapshotToken);
                        _snapshotRevision = _reading.Revision;
                    }

                    DeliverResults();
                    SendSnapshots(now);
                }
            }
            catch (Exception) when (_stopping)
            {
                // Stop closed the listener under us
            }
            catch (Exception ex)
            {
                _faulted = true;
                YargLogger.LogException(ex, "Setlist socket failed");
            }
            finally
            {
                _listener.Close();
                foreach (var client in _clients)
                {
                    if (client.State != ClientState.Free)
                    {
                        Drop(client);
                    }
                }
            }
        }

        private void AcceptClients(long now)
        {
            if (now - _acceptWindowStart >= Stopwatch.Frequency)
            {
                _acceptWindowStart = now;
                _acceptsInWindow = 0;
            }

            // Past these limits connections wait in the listen backlog, and the OS refuses them once it's full, so a
            // connect loop costs at most a few accepted sockets a second.
            while (_acceptsInWindow < ACCEPTS_PER_SECOND && CountClients(ClientState.Handshake) < MAX_PENDING &&
                _listener.Poll(0, SelectMode.SelectRead))
            {
                var client = FindFreeClient();
                if (client is null)
                {
                    return;
                }

                _acceptsInWindow++;
                Socket socket = null;
                try
                {
                    socket = _listener.Accept();
                    socket.NoDelay = true;
                    socket.SendBufferSize = SEND_BUFFER_SIZE;
                    socket.SendTimeout = SEND_TIMEOUT_MS;
                }
                catch (SocketException)
                {
                    // Reset before we got to it; on macOS setting options on a reset socket fails too
                    socket?.Close();
                    continue;
                }

                client.Socket = socket;
                client.State = ClientState.Handshake;
                client.Deadline = now + HandshakeTimeout;
                client.Generation++;
                client.Version = 0;
                client.ReceivedCount = 0;
                client.SentRevision = 0;
                client.SentBytes = 0;
                client.AckedBytes = 0;
                client.SnapshotEnd = 0;
                client.QueuedCommands = 0;
                Array.Clear(client.RecentTokens, 0, RECENT_FRAMES);
                Array.Clear(client.RecentEnds, 0, RECENT_FRAMES);
            }
        }

        private void ServiceClients(long now)
        {
            // A rotating start, so the first slots can't take the whole command queue every time
            for (int n = 0; n < _clients.Length; n++)
            {
                var client = _clients[(_serviceStart + n) % _clients.Length];
                if (client.State == ClientState.Free)
                {
                    continue;
                }

                bool expired = client.State switch
                {
                    ClientState.Handshake or ClientState.Closing => now > client.Deadline,
                    ClientState.Ready => client.AwaitingAck && client.SentRevision != _snapshotRevision &&
                        now - client.SentAt > AckTimeout,
                    _ => false,
                };

                if (expired || !Receive(client))
                {
                    Drop(client);
                    continue;
                }

                switch (client.State)
                {
                    case ClientState.Handshake:
                        HandleHello(client, now);
                        break;
                    case ClientState.Ready:
                        if (!HandleFrames(client))
                        {
                            Drop(client);
                        }
                        break;
                    case ClientState.Closing:
                        client.ReceivedCount = 0;
                        break;
                }
            }

            _serviceStart = (_serviceStart + 1) % _clients.Length;
        }

        /// <summary>
        /// Reads whatever has arrived without blocking. Returns false when the client has gone.
        /// </summary>
        private static bool Receive(Client client)
        {
            // A full buffer holds a command waiting for queue space; TCP holds the rest until it's taken
            int space = client.Received.Length - client.ReceivedCount;
            if (space == 0 || !client.Socket.Poll(0, SelectMode.SelectRead))
            {
                return true;
            }

            int read = client.Socket.Receive(client.Received, client.ReceivedCount, space, SocketFlags.None,
                out var error);
            if (error != SocketError.Success || read == 0)
            {
                return false;
            }

            client.ReceivedCount += read;
            return true;
        }

        private void HandleHello(Client client, long now)
        {
            var received = client.Received.AsSpan(0, client.ReceivedCount);
            if (!IsHelloPrefix(received))
            {
                Drop(client);
                return;
            }

            if (received.Length < CLIENT_HELLO_SIZE)
            {
                return;
            }

            ReadClientHello(received, out ushort minVersion, out ushort maxVersion);
            ushort version = ChooseVersion(minVersion, maxVersion);
            var status = HelloStatus.Ok;
            if (version == 0)
            {
                status = HelloStatus.NoCommonVersion;
            }
            else if (CountClients(ClientState.Ready) >= MAX_CLIENTS)
            {
                status = HelloStatus.Busy;
                version = 0;
            }

            int length = WriteServerHello(_helloBuffer, version, status, _sessionNonce);
            client.Socket.Send(_helloBuffer, 0, length, SocketFlags.None, out var error);
            if (error != SocketError.Success)
            {
                Drop(client);
                return;
            }

            if (status != HelloStatus.Ok)
            {
                try
                {
                    client.Socket.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException)
                {
                    Drop(client);
                    return;
                }

                client.State = ClientState.Closing;
                client.Deadline = now + LingerTimeout;
                client.ReceivedCount = 0;
                return;
            }

            client.State = ClientState.Ready;
            client.Version = version;
            Log("Setlist socket client connected");

            int extra = client.ReceivedCount - CLIENT_HELLO_SIZE;
            Buffer.BlockCopy(client.Received, CLIENT_HELLO_SIZE, client.Received, 0, extra);
            client.ReceivedCount = extra;
            if (!HandleFrames(client))
            {
                Drop(client);
            }
        }

        /// <summary>
        /// Consumes complete frames, stopping at a command that has no queue space yet. Returns false on a frame
        /// that breaks the protocol.
        /// </summary>
        private bool HandleFrames(Client client)
        {
            int clientIndex = Array.IndexOf(_clients, client);
            int offset = 0;
            while (true)
            {
                var remaining = client.Received.AsSpan(offset, client.ReceivedCount - offset);
                int size = ReadClientFrame(remaining, out var type, out var payload);
                if (size < 0)
                {
                    return false;
                }

                if (size == 0)
                {
                    break;
                }

                if (type == FrameType.Ack && TryReadAck(payload, out uint token))
                {
                    Acknowledge(client, token);
                }
                else if (type == FrameType.Command && client.Version >= 2)
                {
                    if (!TryReadCommand(payload, out var command))
                    {
                        return false;
                    }

                    if (client.QueuedCommands >= MAX_QUEUED_PER_CLIENT || !TryQueueCommand(clientIndex, client, command))
                    {
                        break;
                    }

                    client.QueuedCommands++;
                }

                offset += size;
            }

            Buffer.BlockCopy(client.Received, offset, client.Received, 0, client.ReceivedCount - offset);
            client.ReceivedCount -= offset;
            return true;
        }

        /// <summary>
        /// Advances how far the client has read to the end of the frame <paramref name="token"/> was sent in. An
        /// unknown token, one too old or never sent, changes nothing.
        /// </summary>
        private static void Acknowledge(Client client, uint token)
        {
            for (int i = 0; i < RECENT_FRAMES; i++)
            {
                if (client.RecentEnds[i] > 0 && client.RecentTokens[i] == token)
                {
                    client.AckedBytes = Math.Max(client.AckedBytes, client.RecentEnds[i]);
                    return;
                }
            }
        }

        private bool TryQueueCommand(int clientIndex, Client client, in Command command)
        {
            lock (_queueLock)
            {
                if (_commandCount == QUEUE_SIZE)
                {
                    return false;
                }

                _commands[(_commandHead + _commandCount) % QUEUE_SIZE] = new QueuedCommand
                {
                    Client = clientIndex,
                    Generation = client.Generation,
                    Command = command,
                };
                _commandCount++;
                return true;
            }
        }

        private bool TryTakeResult(out QueuedResult result)
        {
            lock (_queueLock)
            {
                if (_resultCount == 0)
                {
                    result = default;
                    return false;
                }

                result = _results[_resultHead];
                _resultHead = (_resultHead + 1) % QUEUE_SIZE;
                _resultCount--;
                return true;
            }
        }

        private bool TakeLatestSnapshot()
        {
            lock (_swapLock)
            {
                if (!_hasLatest)
                {
                    return false;
                }

                (_reading, _latest) = (_latest, _reading);
                _hasLatest = false;
            }

            return true;
        }

        private void DeliverResults()
        {
            while (TryTakeResult(out var result))
            {
                var client = _clients[result.Client];
                if (client.State != ClientState.Ready || client.Generation != result.Generation)
                {
                    continue;
                }

                client.QueuedCommands--;

                // A client this far behind is sending commands without reading the answers
                if (client.Unacked + RESULT_FRAME_SIZE > MAX_UNACKED_BYTES)
                {
                    Drop(client);
                    continue;
                }

                uint token = NextToken();
                int length = WriteResult(_resultBuffer, result.RequestId, result.Status, result.EntryId,
                    result.Revision, token);
                if (!Send(client, _resultBuffer, length, token))
                {
                    Drop(client);
                }
            }
        }

        private void SendSnapshots(long now)
        {
            foreach (var client in _clients)
            {
                if (client.State != ClientState.Ready || client.AwaitingAck || client.SentRevision == _snapshotRevision)
                {
                    continue;
                }

                var buffer = client.Version >= 2 ? _snapshotV2 : _snapshotV1;
                int length = client.Version >= 2 ? _snapshotV2Length : _snapshotV1Length;
                if (length == 0)
                {
                    continue;
                }

                // Mono's Send doesn't return until every byte is handed to the OS, so a send that doesn't fit would
                // block this thread for as long as the client stalls. Acks echo a token the client can only get by
                // reading, so what it hasn't acknowledged is really unread, and keeping that under
                // MAX_UNACKED_BYTES means the send fits in SEND_BUFFER_SIZE. Until results are acknowledged, the
                // snapshot waits.
                if (client.Unacked + length > MAX_UNACKED_BYTES)
                {
                    continue;
                }

                if (!Send(client, buffer, length, _snapshotToken))
                {
                    Drop(client);
                    continue;
                }

                client.SentRevision = _snapshotRevision;
                client.SentAt = now;
                client.SnapshotEnd = client.SentBytes;
            }
        }

        private static bool Send(Client client, byte[] buffer, int length, uint token)
        {
            client.Socket.Send(buffer, 0, length, SocketFlags.None, out var error);
            if (error != SocketError.Success)
            {
                return false;
            }

            client.SentBytes += length;
            client.RecentTokens[client.RecentNext] = token;
            client.RecentEnds[client.RecentNext] = client.SentBytes;
            client.RecentNext = (client.RecentNext + 1) % RECENT_FRAMES;
            return true;
        }

        /// <summary>
        /// A token a client can't predict without reading the frame that carries it.
        /// </summary>
        private uint NextToken()
        {
            if (_randomOffset > _randomBytes.Length - 4)
            {
                _random.GetBytes(_randomBytes);
                _randomOffset = 0;
            }

            uint token = BitConverter.ToUInt32(_randomBytes, _randomOffset);
            _randomOffset += 4;
            return token;
        }

        private void Drop(Client client)
        {
            if (client.State == ClientState.Ready)
            {
                Log("Setlist socket client disconnected");
            }

            client.Socket.Close();
            client.Socket = null;
            client.State = ClientState.Free;
        }

        private Client FindFreeClient()
        {
            foreach (var client in _clients)
            {
                if (client.State == ClientState.Free)
                {
                    return client;
                }
            }

            return null;
        }

        private int CountClients(ClientState state)
        {
            int count = 0;
            foreach (var client in _clients)
            {
                if (client.State == state)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// At most one line a second, so a client that connects in a loop can't flood the log.
        /// </summary>
        private void Log(string message)
        {
            long now = Stopwatch.GetTimestamp();
            if (now - _lastLogAt < Stopwatch.Frequency)
            {
                return;
            }

            _lastLogAt = now;
            YargLogger.LogInfo(message);
        }
    }
}
