using System;
using System.Runtime.InteropServices;
using ManagedBass;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Direct imports of the BASS functions that take a callback. Callers must keep the delegates they pass
    ///     alive for as long as BASS may call them, e.g. in static fields.
    /// </summary>
    /// <remarks>
    ///     ManagedBass's wrappers for these also register their own BASS_SYNC_FREE callback
    ///     (<c>ChannelReferences.Callback</c>) to keep delegates alive. That method has no
    ///     <c>[MonoPInvokeCallback]</c>, which IL2CPP needs before it can marshal a method to native code, so the
    ///     wrappers throw NotSupportedException in IL2CPP players.
    /// </remarks>
    internal static class BassCallbackImports
    {
        [DllImport("bass", EntryPoint = "BASS_StreamCreateFileUser")]
        public static extern int StreamCreateFileUser(StreamSystem system, BassFlags flags,
            [In] FileProcedures procedures, IntPtr user);

        [DllImport("bass", EntryPoint = "BASS_ChannelSetSync")]
        public static extern int ChannelSetSync(int handle, SyncFlags type, long parameter, SyncProcedure procedure,
            IntPtr user);

        [DllImport("bassmix", EntryPoint = "BASS_Mixer_ChannelSetSync")]
        public static extern int MixerChannelSetSync(int handle, SyncFlags type, long parameter,
            SyncProcedure procedure, IntPtr user);

        /// <param name="period">Milliseconds between <paramref name="procedure"/> calls; BASS reads it from the
        /// high word of the flags, as in ManagedBass's equivalent overload.</param>
        public static int RecordStart(int frequency, int channels, BassFlags flags, int period,
            RecordProcedure procedure, IntPtr user) =>
            BASS_RecordStart(frequency, channels, (ushort) flags | (period << 16), procedure, user);

        [DllImport("bass")]
        private static extern int BASS_RecordStart(int frequency, int channels, int flags, RecordProcedure procedure,
            IntPtr user);
    }
}
