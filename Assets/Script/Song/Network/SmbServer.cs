using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SMBLibrary;
using SMBLibrary.Client;

namespace YARG.Song.Network
{
    /// <param name="generation">The session's <see cref="SmbSession.Generation"/> while the request runs</param>
    internal delegate NTStatus SmbRequest(ISMBFileStore store, int generation);

    /// <summary>
    /// A pool of sessions to one server. Each session carries one request at a time, and the pool size caps
    /// the requests in flight, which keeps the scanner's nested Parallel.ForEach from flooding the server.
    /// </summary>
    internal sealed class SmbServer : IDisposable
    {
        private static readonly TimeSpan CONNECT_FAILURE_BACKOFF = TimeSpan.FromSeconds(10);

        private readonly string _host;
        private readonly Func<SmbSession> _sessionFactory;
        private readonly int _maxSessions;
        private readonly SemaphoreSlim _gate;
        private readonly List<SmbSession> _sessions = new();
        private int _nextSession;

        private readonly object _failureLock = new();
        private SmbConnectException _connectFailure;
        private DateTime _connectFailureTime;

        internal SmbServer(string host, Func<SmbSession> sessionFactory, int maxSessions)
        {
            _host = host;
            _sessionFactory = sessionFactory;
            _maxSessions = maxSessions;
            _gate = new SemaphoreSlim(maxSessions, maxSessions);
        }

        /// <summary>
        /// Runs <paramref name="request"/> on <paramref name="pinned"/>, or on any free session when it is null.
        /// After a dropped connection or a transient failure, the session reconnects and the request runs once
        /// more; it must therefore be safe to repeat, and must reopen any handle from an older generation.
        /// </summary>
        /// <exception cref="SmbConnectException">The server is unreachable or refused the login</exception>
        /// <exception cref="IOException">The retry failed too</exception>
        internal NTStatus Run(SmbSession pinned, string share, SmbRequest request, out SmbSession used)
        {
            for (int attempt = 0; ; attempt++)
            {
                _gate.Wait();
                var session = pinned ?? EnterAnySession();
                if (pinned != null)
                {
                    Monitor.Enter(pinned.Lock);
                }

                NTStatus status;
                Exception error = null;
                try
                {
                    status = TryGetStore(session, share, out var store);
                    if (status == NTStatus.STATUS_SUCCESS)
                    {
                        status = request(store, session.Generation);
                    }
                }
                catch (SmbConnectException)
                {
                    throw;
                }
                catch (Exception e) when (!(e is FileNotFoundException || e is DirectoryNotFoundException || e is UnauthorizedAccessException))
                {
                    error = e;
                    status = NTStatus.STATUS_INVALID_SMB;
                }
                finally
                {
                    Monitor.Exit(session.Lock);
                    _gate.Release();
                }

                used = session;
                if (!IsTransient(status))
                {
                    return status;
                }

                lock (session.Lock)
                {
                    session.Reset();
                }

                if (attempt > 0)
                {
                    throw new IOException($"SMB request to '{_host}' failed: {status}", error);
                }
            }
        }

        internal static bool IsTransient(NTStatus status)
        {
            switch (status)
            {
                case NTStatus.STATUS_INVALID_SMB:
                case NTStatus.STATUS_IO_TIMEOUT:
                case NTStatus.STATUS_NETWORK_NAME_DELETED:
                case NTStatus.STATUS_USER_SESSION_DELETED:
                case NTStatus.STATUS_INVALID_HANDLE:
                case NTStatus.STATUS_FILE_CLOSED:
                    return true;
                default:
                    return false;
            }
        }

        public void Dispose()
        {
            lock (_sessions)
            {
                foreach (var session in _sessions)
                {
                    lock (session.Lock)
                    {
                        session.Reset();
                    }
                }
                _sessions.Clear();
            }
        }

        private NTStatus TryGetStore(SmbSession session, string share, out ISMBFileStore store)
        {
            if (session.IsConnected)
            {
                return session.TryGetStore(share, out store);
            }

            lock (_failureLock)
            {
                if (_connectFailure != null && DateTime.UtcNow - _connectFailureTime < CONNECT_FAILURE_BACKOFF)
                {
                    throw new SmbConnectException(_connectFailure.Message, _connectFailure);
                }
            }

            try
            {
                return session.TryGetStore(share, out store);
            }
            catch (SmbConnectException e)
            {
                lock (_failureLock)
                {
                    _connectFailure = e;
                    _connectFailureTime = DateTime.UtcNow;
                }
                throw;
            }
        }

        private SmbSession EnterAnySession()
        {
            SmbSession session;
            lock (_sessions)
            {
                foreach (var candidate in _sessions)
                {
                    if (Monitor.TryEnter(candidate.Lock))
                    {
                        return candidate;
                    }
                }

                if (_sessions.Count < _maxSessions)
                {
                    session = _sessionFactory();
                    Monitor.Enter(session.Lock);
                    _sessions.Add(session);
                    return session;
                }

                // Several pinned requests can queue on one session, so a gate slot doesn't guarantee a free one.
                session = _sessions[_nextSession++ % _sessions.Count];
            }
            Monitor.Enter(session.Lock);
            return session;
        }
    }
}
