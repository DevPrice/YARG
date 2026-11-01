using System;
using System.IO;
using AOT;
using ManagedBass;

namespace YARG.Audio.BASS
{
    public class BassStreamProcedures : FileProcedures
    {
        private static readonly BassCallbackTargets<BassStreamProcedures> Targets = new();

        private static readonly FileCloseProcedure  CloseProcedure  = OnClose;
        private static readonly FileLengthProcedure LengthProcedure = OnLength;
        private static readonly FileReadProcedure   ReadProcedure   = OnRead;
        private static readonly FileSeekProcedure   SeekProcedure   = OnSeek;

        /// <summary>
        ///     The procedures of every instance, as a plain <see cref="FileProcedures"/> that BASS tells apart by
        ///     <see cref="User"/>. Pass this rather than an instance: the .NET marshaller rejects derived types.
        /// </summary>
        public static readonly FileProcedures Shared = new()
        {
            Close = CloseProcedure,
            Length = LengthProcedure,
            Read = ReadProcedure,
            Seek = SeekProcedure,
        };

        private readonly Stream _stream;
        private readonly long _start;
        private readonly long _length;

        public BassStreamProcedures(Stream stream)
        {
            _stream = stream;
            _start = stream.Position;
            _length = stream.Length - _start;

            User = Targets.Add(this);

            Close = CloseProcedure;
            Length = LengthProcedure;
            Read = ReadProcedure;
            Seek = SeekProcedure;
        }

        /// <summary>
        ///     The user pointer to pass to BASS with these procedures (or <see cref="Shared"/>). It is released
        ///     when BASS closes the file; call <see cref="ReleaseUser"/> if stream creation fails.
        /// </summary>
        public IntPtr User { get; }

        public void ReleaseUser() => Targets.Remove(User);

        [MonoPInvokeCallback(typeof(FileCloseProcedure))]
        private static void OnClose(IntPtr user)
        {
            var procedures = Targets.Get(user);
            if (procedures == null)
            {
                return;
            }

            Targets.Remove(user);
            procedures._stream.Close();
        }

        [MonoPInvokeCallback(typeof(FileLengthProcedure))]
        private static long OnLength(IntPtr user) => Targets.Get(user)?._length ?? 0;

        [MonoPInvokeCallback(typeof(FileReadProcedure))]
        private static int OnRead(IntPtr buffer, int length, IntPtr user)
        {
            try
            {
                var procedures = Targets.Get(user);
                if (procedures == null)
                {
                    return 0;
                }

                unsafe
                {
                    return procedures._stream.Read(new Span<byte>((byte*) buffer, length));
                }
            }
            catch
            {
                return 0;
            }
        }

        [MonoPInvokeCallback(typeof(FileSeekProcedure))]
        private static bool OnSeek(long offset, IntPtr user)
        {
            try
            {
                var procedures = Targets.Get(user);
                if (procedures == null)
                {
                    return false;
                }

                procedures._stream.Seek(offset + procedures._start, SeekOrigin.Begin);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
