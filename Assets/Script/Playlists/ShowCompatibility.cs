using YARG.Core;
using YARG.Core.Song;
using YARG.Player;

namespace YARG.Playlists
{
    /// <summary>
    /// What a song needs to be played in a show. Difficulty Select limits each player's instrument, difficulty and
    /// harmony part to what every song in the setlist has, and gameplay assumes they exist.
    /// </summary>
    public static class ShowCompatibility
    {
        public static bool HasPart(SongEntry song, Instrument instrument)
        {
            return song.HasInstrument(instrument) || instrument switch
            {
                // Allow 5 -> 4-lane conversions to be played on 4-lane
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => song.HasInstrument(Instrument.FiveLaneDrums),
                // Allow 4 -> 5-lane conversions to be played on 5-lane
                Instrument.FiveLaneDrums => song.HasInstrument(Instrument.ProDrums),
                _ => false
            };
        }

        public static bool HasDifficulty(SongEntry song, Instrument instrument, Difficulty difficulty)
        {
            // For vocals, insert special difficulties
            if (instrument is Instrument.Vocals or Instrument.Harmony or Instrument.PartyVocals)
            {
                return difficulty is not Difficulty.ExpertPlus;
            }

            // For PK, disallow beginner
            if (instrument is Instrument.ProKeys && difficulty is Difficulty.Beginner)
            {
                return false;
            }

            return song[instrument][difficulty] || instrument switch
            {
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => song[Instrument.FiveLaneDrums][difficulty],
                Instrument.FiveLaneDrums => song[Instrument.ProDrums][difficulty],
                _ => false
            };
        }

        /// <summary>
        /// Whether every player who isn't sitting out can play <paramref name="song"/> with the part they're on.
        /// </summary>
        public static bool FitsCurrentPlayers(SongEntry song)
        {
            var players = PlayerContainer.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player.SittingOut)
                {
                    continue;
                }

                var profile = player.Profile;
                var instrument = profile.CurrentInstrument;
                if (!HasPart(song, instrument) || !HasDifficulty(song, instrument, profile.CurrentDifficulty))
                {
                    return false;
                }

                if ((instrument is Instrument.Vocals or Instrument.Harmony or Instrument.PartyVocals) &&
                    profile.HarmonyIndex >= song.VocalsCount)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
