using System;
using YARG.Core.Song;

namespace YARG.Playlists
{
    public enum SetlistCommandOp : byte
    {
        Add,
        Remove,
        Move,
        Clear,
    }

    /// <summary>
    /// An edit an outside app asks for, independent of how it arrived.
    /// </summary>
    public struct SetlistCommand
    {
        public SetlistCommandOp Op;

        /// <summary>
        /// The song to add.
        /// </summary>
        public HashWrapper Hash;

        /// <summary>
        /// The entry to remove or move.
        /// </summary>
        public uint EntryId;

        public SetlistPosition Position;

        /// <summary>
        /// The entry to add or move after, when <see cref="Position"/> is <see cref="SetlistPosition.After"/>.
        /// </summary>
        public uint AnchorId;
    }

    /// <summary>
    /// Applies outside apps' edits, with the rules that apply only to them. Main thread only.
    /// </summary>
    public static class SetlistCommands
    {
        /// <summary>
        /// Adds stop here. The setlist UI has no limit.
        /// </summary>
        public const int MAX_SIZE = 500;

        /// <summary>
        /// Raised after a command adds a song.
        /// </summary>
        public static event Action SongAdded;

        /// <summary>
        /// Returns the outcome and, through <paramref name="entryId"/>, the entry it concerns: the new or existing
        /// entry for an add, otherwise the one named.
        /// </summary>
        public static SetlistEditResult Apply(in SetlistCommand command, out uint entryId)
        {
            entryId = command.EntryId;

            // Edits while the setlist is inactive would be wiped before anyone sees them, so they're refused, not
            // queued
            if (!Setlist.IsActive)
            {
                return SetlistEditResult.Inactive;
            }

            var result = command.Op switch
            {
                SetlistCommandOp.Add =>
                    Setlist.TryAdd(command.Hash, command.Position, command.AnchorId, MAX_SIZE, out entryId),
                SetlistCommandOp.Remove => Setlist.TryRemove(command.EntryId),
                SetlistCommandOp.Move   => Setlist.TryMove(command.EntryId, command.Position, command.AnchorId),
                _                       => Setlist.ClearUpcoming(),
            };

            if (command.Op == SetlistCommandOp.Add && result == SetlistEditResult.Ok)
            {
                SongAdded?.Invoke();
            }

            return result;
        }
    }
}
