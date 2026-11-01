using System;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;
using YARG.Core.Logging;
using YARG.Localization;
using YARG.Menu.Persistent;
using YARG.Playlists;
using YARG.Settings;
using YARG.Song;

namespace YARG.Integration
{
    /// <summary>
    /// Pushes the live setlist to apps on this computer over a loopback socket while its setting is on.
    /// </summary>
    public class SetlistSocket : MonoSingleton<SetlistSocket>
    {
        // Next to the Data Stream's 36107
        public const int PORT = 36108;

        // A port another app holds, or a server that failed, is tried again this often, without another toast
        private const float RETRY_SECONDS = 10f;

        private SetlistSocketIO _server;
        private float _retryAt;
        private bool  _toasted;

        // Each command makes a few linear passes over the setlist (syncing IDs, finding its entries and the current
        // song), so this caps a frame at a few dozen passes. Publishing adds one more, only on a frame with a change.
        private const int MAX_COMMANDS_PER_FRAME = 8;

        private readonly List<SetlistEntry> _entries = new();
        private bool _published;
        private int  _publishedSetlistRevision;
        private bool _publishedActive;
        private int  _publishedLibraryRevision;

        private readonly SetlistSocketIO.QueuedResult[] _frameResults =
            new SetlistSocketIO.QueuedResult[MAX_COMMANDS_PER_FRAME];

        private void LateUpdate()
        {
            UpdateServer();
            if (_server is null)
            {
                return;
            }

            // Results name the snapshot revision that includes them, so publish between applying and answering
            int resultCount = ApplyCommands();
            PublishIfChanged();
            PostResults(resultCount);
        }

        // Also runs before an editor script reload, which skips OnDestroy
        private void OnDisable()
        {
            StopServer();
        }

        private void UpdateServer()
        {
            var settings = SettingsManager.Settings;
            if (settings is null || !settings.SetlistSocketEnable.Value)
            {
                StopServer();
                _retryAt = 0;
                _toasted = false;
                return;
            }

            if (_server is { Faulted: true })
            {
                StopServer();
                _retryAt = Time.unscaledTime + RETRY_SECONDS;
            }

            if (_server is not null || Time.unscaledTime < _retryAt)
            {
                return;
            }

            try
            {
                _server = SetlistSocketIO.Start(PORT);
                _published = false;
                _toasted = false;
                YargLogger.LogFormatInfo("Setlist socket listening on 127.0.0.1:{0}", PORT);
            }
            catch (SocketException ex)
            {
                _retryAt = Time.unscaledTime + RETRY_SECONDS;
                if (!_toasted)
                {
                    _toasted = true;
                    YargLogger.LogException(ex, $"Failed to open the setlist socket on port {PORT}");
                    ToastManager.ToastError(Localize.KeyFormat("Menu.Toast.SetlistSocketPortInUse", PORT));
                }
            }
        }

        private void StopServer()
        {
            if (_server is null)
            {
                return;
            }

            _server.Stop();
            _server = null;
            YargLogger.LogInfo("Setlist socket closed");
        }

        private void PublishIfChanged()
        {
            int setlistRevision = Setlist.Revision;
            bool active = Setlist.IsActive;
            // A rescan or a content-rating change can stop setlist songs resolving without touching the setlist
            int libraryRevision = SongContainer.LibraryRevision;

            if (_published && setlistRevision == _publishedSetlistRevision && active == _publishedActive &&
                libraryRevision == _publishedLibraryRevision)
            {
                return;
            }

            _published = true;
            _publishedSetlistRevision = setlistRevision;
            _publishedActive = active;
            _publishedLibraryRevision = libraryRevision;

            // An inactive setlist is about to be cleared, so clients see it empty
            if (active)
            {
                Setlist.ResolveEntriesInto(_entries);
            }
            else
            {
                _entries.Clear();
            }

            var snapshot = _server.PendingSnapshot;
            int staleCount = snapshot.EntryCount;

            snapshot.Active = active;
            snapshot.ShowInProgress = Setlist.IsPlaying;
            snapshot.CurrentIndex = Setlist.Index;
            snapshot.CurrentEntryId = Setlist.CurrentEntryId;
            snapshot.TotalCount = _entries.Count;
            snapshot.EntryCount = Math.Min(_entries.Count, snapshot.Entries.Length);

            for (int i = 0; i < snapshot.EntryCount; i++)
            {
                var song = _entries[i].Song;
                ref var entry = ref snapshot.Entries[i];
                entry.Id = _entries[i].Id;
                entry.Hash = song.Hash;
                entry.LengthMs = (uint) Math.Clamp(song.SongLengthMilliseconds, 0, uint.MaxValue);
                entry.Title = song.Name.Original;
                entry.Artist = song.Artist.Original;
                entry.Album = song.Album.Original;
            }

            // Don't keep strings of songs that have left the setlist alive
            if (staleCount > snapshot.EntryCount)
            {
                Array.Clear(snapshot.Entries, snapshot.EntryCount, staleCount - snapshot.EntryCount);
            }

            _server.Publish();
        }

        /// <summary>
        /// Applies up to <see cref="MAX_COMMANDS_PER_FRAME"/> queued commands, collecting their results in
        /// <see cref="_frameResults"/>. Returns how many.
        /// </summary>
        private int ApplyCommands()
        {
            int limit = Math.Min(MAX_COMMANDS_PER_FRAME, _server.FreeResultSlots);
            int count = 0;

            while (count < limit && _server.TryTakeCommand(out var queued))
            {
                var command = queued.Command;
                uint entryId = command.Edit.EntryId;
                var status = command.Malformed
                    ? SetlistProtocol.ResultStatus.Malformed
                    : SetlistProtocol.ToStatus(SetlistCommands.Apply(command.Edit, out entryId));

                _frameResults[count++] = new SetlistSocketIO.QueuedResult
                {
                    Client = queued.Client,
                    Generation = queued.Generation,
                    RequestId = command.RequestId,
                    Status = status,
                    EntryId = entryId,
                };
            }

            return count;
        }

        private void PostResults(int count)
        {
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                _frameResults[i].Revision = _server.PublishedRevision;
                _server.PostResult(_frameResults[i]);
            }

            _server.SendResults();
        }
    }
}
