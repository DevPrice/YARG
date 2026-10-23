using System.Collections.Generic;
using YARG.Core.Song;
using YARG.Song;

namespace YARG.Playlists
{
    public readonly struct SetlistEntry
    {
        public readonly uint      Id;
        public readonly SongEntry Song;

        public SetlistEntry(uint id, SongEntry song)
        {
            Id = id;
            Song = song;
        }
    }

    public enum SetlistPosition : byte
    {
        End,
        Start,
        After,
    }

    public enum SetlistEditResult
    {
        Ok,
        EntryNotFound,
        AnchorNotFound,
        SongNotFound,
        AlreadyInSetlist,
        Locked,
        Full,
        Incompatible,
    }

    /// <summary>
    /// Stable entry IDs and the edits outside apps can make. None of these allocate once the lists have grown.
    /// </summary>
    public static partial class Setlist
    {
        /// <summary>
        /// <see cref="TryAdd"/> stops here. The setlist UI has no limit.
        /// </summary>
        public const int MAX_REMOTE_SIZE = 500;

        /// <summary>
        /// Increments when the songs change, but not on playback or <see cref="IsActive"/> changes.
        /// </summary>
        public static int ContentRevision { get; private set; }

        /// <summary>
        /// While a show's first song hasn't started, players are still choosing parts in Difficulty Select, which
        /// limits each choice to what every song in the setlist has. Edits then would bypass those limits.
        /// </summary>
        public static bool IsPickingParts => IsPlaying && _menuSceneLoaded;

        private static bool _menuSceneLoaded;

        // IDs are matched to songs lazily, by hash, so every edit path (the setlist UI, sorts that rewrite
        // SongHashes, rescans) keeps working without knowing about IDs. _idHashes is SongHashes as of the last
        // sync, and _ids runs parallel to both.
        private static readonly List<HashWrapper>              _idHashes = new();
        private static readonly List<uint>                     _ids      = new();
        private static readonly Dictionary<HashWrapper, uint> _idByHash = new();
        private static uint _nextId = 1;

        /// <summary>
        /// Called by <c>MenuManager</c> when the menu scene loads and unloads.
        /// </summary>
        public static void SetMenuSceneLoaded(bool loaded) => _menuSceneLoaded = loaded;

        /// <summary>
        /// <see cref="ResolveSongsInto"/> with each song's entry ID.
        /// </summary>
        public static void ResolveEntriesInto(List<SetlistEntry> entries)
        {
            SyncEntryIds();
            entries.Clear();

            var hashes = Playlist.SongHashes;
            for (int i = 0; i < hashes.Count; i++)
            {
                if (SongContainer.SongsByHash.TryGetValue(hashes[i], out var songs))
                {
                    entries.Add(new SetlistEntry(_ids[i], songs[0]));
                }
            }
        }

        /// <summary>
        /// The ID of the song being played, or 0 when no show is in progress.
        /// </summary>
        public static uint CurrentEntryId
        {
            get
            {
                SyncEntryIds();
                int position = CurrentPosition();
                return position < 0 ? 0 : _ids[position];
            }
        }

        public static SetlistEditResult TryAdd(HashWrapper hash, SetlistPosition where, uint anchorId,
            out uint entryId)
        {
            SyncEntryIds();
            entryId = 0;

            if (IsPickingParts)
            {
                return SetlistEditResult.Locked;
            }

            if (!SongContainer.SongsByHash.TryGetValue(hash, out var songs))
            {
                return SetlistEditResult.SongNotFound;
            }

            int existing = Playlist.SongHashes.IndexOf(hash);
            if (existing >= 0)
            {
                entryId = _ids[existing];
                return SetlistEditResult.AlreadyInSetlist;
            }

            if (Playlist.SongHashes.Count >= MAX_REMOTE_SIZE)
            {
                return SetlistEditResult.Full;
            }

            if (IsPlaying && !ShowCompatibility.FitsCurrentPlayers(songs[0]))
            {
                return SetlistEditResult.Incompatible;
            }

            var result = FindInsertIndex(where, anchorId, CurrentPosition(), out int index);
            if (result != SetlistEditResult.Ok)
            {
                return result;
            }

            entryId = _nextId++;
            Insert(index, hash, entryId);
            OnSongsChanged();
            return SetlistEditResult.Ok;
        }

        public static SetlistEditResult TryRemove(uint entryId)
        {
            SyncEntryIds();

            int position = _ids.IndexOf(entryId);
            if (position < 0)
            {
                return SetlistEditResult.EntryNotFound;
            }

            if (IsPickingParts || IsLocked(position, CurrentPosition()))
            {
                return SetlistEditResult.Locked;
            }

            RemoveAt(position);
            OnSongsChanged();
            return SetlistEditResult.Ok;
        }

        public static SetlistEditResult TryMove(uint entryId, SetlistPosition where, uint anchorId)
        {
            SyncEntryIds();

            int from = _ids.IndexOf(entryId);
            if (from < 0)
            {
                return SetlistEditResult.EntryNotFound;
            }

            int current = CurrentPosition();
            if (IsPickingParts || IsLocked(from, current))
            {
                return SetlistEditResult.Locked;
            }

            if (where == SetlistPosition.After && anchorId == entryId)
            {
                return SetlistEditResult.Ok;
            }

            // Only entries after the current one move, so taking this one out doesn't shift the current position
            var hash = Playlist.SongHashes[from];
            RemoveAt(from);

            var result = FindInsertIndex(where, anchorId, current, out int to);
            Insert(result == SetlistEditResult.Ok ? to : from, hash, entryId);

            if (result == SetlistEditResult.Ok && to != from)
            {
                OnSongsChanged();
            }

            return result;
        }

        /// <summary>
        /// Empties the setlist, or during a show removes every song after the current one.
        /// </summary>
        public static SetlistEditResult ClearUpcoming()
        {
            SyncEntryIds();

            if (IsPickingParts)
            {
                return SetlistEditResult.Locked;
            }

            int current = CurrentPosition();
            if (IsLocked(Playlist.SongHashes.Count, current))
            {
                return SetlistEditResult.Locked;
            }

            int first = current + 1;

            int count = Playlist.SongHashes.Count - first;
            if (count > 0)
            {
                Playlist.SongHashes.RemoveRange(first, count);
                _idHashes.RemoveRange(first, count);
                _ids.RemoveRange(first, count);
                OnSongsChanged();
            }

            return SetlistEditResult.Ok;
        }

        private static SetlistEditResult FindInsertIndex(SetlistPosition where, uint anchorId, int current,
            out int index)
        {
            switch (where)
            {
                case SetlistPosition.Start:
                    index = 0;
                    break;
                case SetlistPosition.After:
                    int anchor = _ids.IndexOf(anchorId);
                    if (anchor < 0)
                    {
                        index = -1;
                        return SetlistEditResult.AnchorNotFound;
                    }

                    index = anchor + 1;
                    break;
                default:
                    index = Playlist.SongHashes.Count;
                    break;
            }

            // Nothing may land at or before the song being played; right after it makes it the next song
            return IsLocked(index, current) ? SetlistEditResult.Locked : SetlistEditResult.Ok;
        }

        // If the song being played can't be found, any position might be at or before it, so all are locked
        private static bool IsLocked(int position, int current) => IsPlaying && (current < 0 || position <= current);

        /// <summary>
        /// The position in <see cref="Playlist.SongHashes"/> of the song being played, or -1 outside a show.
        /// </summary>
        /// <remarks>
        /// <see cref="Index"/> counts only songs that resolve, so hashes that don't are skipped. Edits never touch
        /// positions at or before this one, which keeps <see cref="Index"/> pointing at the same song.
        /// </remarks>
        private static int CurrentPosition()
        {
            if (!IsPlaying)
            {
                return -1;
            }

            var hashes = Playlist.SongHashes;
            int resolved = 0;
            for (int i = 0; i < hashes.Count; i++)
            {
                if (!SongContainer.SongsByHash.ContainsKey(hashes[i]))
                {
                    continue;
                }

                if (resolved == Index)
                {
                    return i;
                }

                resolved++;
            }

            return -1;
        }

        private static void Insert(int index, HashWrapper hash, uint id)
        {
            Playlist.SongHashes.Insert(index, hash);
            _idHashes.Insert(index, hash);
            _ids.Insert(index, id);
        }

        private static void RemoveAt(int index)
        {
            Playlist.SongHashes.RemoveAt(index);
            _idHashes.RemoveAt(index);
            _ids.RemoveAt(index);
        }

        private static void SyncEntryIds()
        {
            var hashes = Playlist.SongHashes;
            if (hashes.Count == _idHashes.Count)
            {
                int i = 0;
                while (i < hashes.Count && hashes[i].Equals(_idHashes[i]))
                {
                    i++;
                }

                if (i == hashes.Count)
                {
                    return;
                }
            }

            // First wins, like the rescan dedup in ReplaceUpdatedSongHashes, and a hash claims an old ID only once
            _idByHash.Clear();
            for (int i = 0; i < _idHashes.Count; i++)
            {
                _idByHash.TryAdd(_idHashes[i], _ids[i]);
            }

            _idHashes.Clear();
            _ids.Clear();
            foreach (var hash in hashes)
            {
                if (!_idByHash.Remove(hash, out uint id))
                {
                    id = _nextId++;
                }

                _idHashes.Add(hash);
                _ids.Add(id);
            }
        }

        /// <summary>
        /// Applies a rescan's hash replacements to the ID bookkeeping too, so renamed songs keep their IDs.
        /// </summary>
        private static void ReplaceIdHashes(IReadOnlyDictionary<HashWrapper, HashWrapper> replacements)
        {
            SyncEntryIds();
            for (int i = 0; i < _idHashes.Count; i++)
            {
                if (replacements.TryGetValue(_idHashes[i], out var replacement))
                {
                    _idHashes[i] = replacement;
                }
            }
        }

        private static void OnSongsChanged()
        {
            ContentRevision++;
            OnChanged();
        }
    }
}
