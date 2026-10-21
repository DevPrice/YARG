using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Client;
using YARG.Core.Logging;

namespace YARG.Song.Network
{
    /// <summary>
    /// Thrown when a server can't be reached or refuses every login. Not retried.
    /// </summary>
    public sealed class SmbConnectException : IOException
    {
        public SmbConnectException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// One logged-in SMB session and its tree connects. Callers must hold <see cref="Lock"/> for every
    /// member, because SMBLibrary's client doesn't synchronize its message IDs or credits.
    /// </summary>
    internal abstract class SmbSession
    {
        internal readonly object Lock = new();

        private readonly Dictionary<string, ISMBFileStore> _trees = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Changes on every (re)connect. Handles opened under an older generation are dead.
        /// </summary>
        internal int Generation { get; private set; }

        internal abstract bool IsConnected { get; }

        /// <exception cref="SmbConnectException"/>
        protected abstract void Connect();

        protected abstract ISMBFileStore TreeConnect(string share, out NTStatus status);

        /// <summary>
        /// Drops the connection without throwing.
        /// </summary>
        protected abstract void Disconnect();

        /// <summary>
        /// Replaces the connection with one logged in as the next identity, after a share refused this one.
        /// </summary>
        /// <returns>False, leaving the current connection in place, when no other identity logs in</returns>
        protected virtual bool TryNextLogin()
        {
            return false;
        }

        internal NTStatus TryGetStore(string share, out ISMBFileStore store)
        {
            if (!IsConnected)
            {
                _trees.Clear();
                Connect();
                Generation++;
            }

            if (_trees.TryGetValue(share, out store))
            {
                return NTStatus.STATUS_SUCCESS;
            }

            store = TreeConnect(share, out var status);
            while (status == NTStatus.STATUS_ACCESS_DENIED && TryNextLogin())
            {
                _trees.Clear();
                Generation++;
                store = TreeConnect(share, out status);
            }

            if (status == NTStatus.STATUS_SUCCESS && store != null)
            {
                _trees.Add(share, store);
            }
            return status;
        }

        internal void Reset()
        {
            _trees.Clear();
            Disconnect();
        }
    }

    internal sealed class Smb2Session : SmbSession
    {
        // Anonymous first, then guest. Samba's "map to guest = Bad User" (TrueNAS guest shares) maps only
        // unknown accounts to guest, so the last identity is a name that can't exist. An anonymous session can
        // log in yet be refused by the share, so a refused tree connect moves on to the next identity.
        private static readonly string[] LOGIN_USERS = { "", "guest", "yarg-guest-nobody" };

        private readonly string _host;
        private SMB2Client _client;
        private IPAddress _address;
        private int _identity;

        internal Smb2Session(string host)
        {
            _host = host;
        }

        internal override bool IsConnected => _client != null && _client.IsConnected;

        protected override void Connect()
        {
            Disconnect();

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(_host);
            }
            catch (Exception e)
            {
                throw new SmbConnectException($"Could not resolve SMB server '{_host}'", e);
            }

            var ordered = new List<IPAddress>(addresses.Length);
            ordered.AddRange(Array.FindAll(addresses, address => address.AddressFamily == AddressFamily.InterNetwork));
            ordered.AddRange(Array.FindAll(addresses, address => address.AddressFamily != AddressFamily.InterNetwork));

            var lastStatus = NTStatus.STATUS_SUCCESS;
            Exception lastError = null;
            foreach (var address in ordered)
            {
                for (int identity = _identity; identity < LOGIN_USERS.Length; identity++)
                {
                    var client = TryLogin(address, LOGIN_USERS[identity], out bool reachable, ref lastStatus, ref lastError);
                    if (client != null)
                    {
                        _client = client;
                        _address = address;
                        _identity = identity;
                        YargLogger.LogFormatInfo("Connected to SMB server {0} at {1}", _host, address);
                        return;
                    }

                    if (!reachable)
                    {
                        break;
                    }
                }
            }

            // Start over from anonymous on the next attempt, in case the server's settings changed.
            _identity = 0;
            string reason = lastStatus != NTStatus.STATUS_SUCCESS
                ? $"guest login refused ({lastStatus})"
                : "no address accepted a connection on port 445";
            throw new SmbConnectException($"Could not connect to SMB server '{_host}': {reason}", lastError);
        }

        protected override bool TryNextLogin()
        {
            var lastStatus = NTStatus.STATUS_SUCCESS;
            Exception lastError = null;
            for (int identity = _identity + 1; identity < LOGIN_USERS.Length; identity++)
            {
                var client = TryLogin(_address, LOGIN_USERS[identity], out bool reachable, ref lastStatus, ref lastError);
                if (client != null)
                {
                    Disconnect();
                    _client = client;
                    _identity = identity;
                    return true;
                }

                if (!reachable)
                {
                    break;
                }
            }
            return false;
        }

        protected override ISMBFileStore TreeConnect(string share, out NTStatus status)
        {
            return _client.TreeConnect(share, out status);
        }

        protected override void Disconnect()
        {
            if (_client != null)
            {
                SafeDisconnect(_client);
                _client = null;
            }
        }

        private static SMB2Client TryLogin(IPAddress address, string user, out bool reachable, ref NTStatus lastStatus, ref Exception lastError)
        {
            reachable = false;
            var client = new SMB2Client();
            try
            {
                if (!client.Connect(address, SMBTransportType.DirectTCPTransport))
                {
                    return null;
                }

                reachable = true;
                lastStatus = client.Login(string.Empty, user, string.Empty);
                if (lastStatus == NTStatus.STATUS_SUCCESS)
                {
                    return client;
                }
            }
            catch (Exception e)
            {
                lastError = e;
            }
            SafeDisconnect(client);
            return null;
        }

        private static void SafeDisconnect(SMB2Client client)
        {
            try
            {
                client.Disconnect();
            }
            catch (Exception)
            {
                // The socket may already be torn down; there is nothing left to release.
            }
        }
    }
}
