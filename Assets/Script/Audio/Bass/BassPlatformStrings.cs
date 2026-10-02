using System;
using System.Runtime.InteropServices;
using ManagedBass;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Converts strings to and from the encoding and form that this platform's build of BASS uses.
    /// </summary>
    internal static class BassPlatformStrings
    {
        /// <summary>
        ///     Gets <see cref="DeviceInfo.Name"/>, decoded the way this platform's BASS encodes it.
        /// </summary>
        public static string GetName(this DeviceInfo info)
        {
#if UNITY_WSA && !UNITY_EDITOR
            // The UWP build of BASS returns device names as UTF-16 whatever BASS_CONFIG_UNICODE says (its built-in
            // "Default" and "No sound" names are wide literals), but ManagedBass decodes them as UTF-8 or ANSI and
            // keeps only the first character. The name pointer is the first field of BASS_DEVICEINFO.
            unsafe
            {
                return Marshal.PtrToStringUni(*(IntPtr*) &info);
            }
#else
            return info.Name;
#endif
        }

        /// <summary>
        ///     Converts a file path into a form that this platform's BASS can open.
        /// </summary>
        public static string ToBassPath(string path)
        {
#if UNITY_WSA && !UNITY_EDITOR
            // The UWP build of BASS opens files through StorageFile.GetFileFromPathAsync, which rejects forward
            // slashes (https://learn.microsoft.com/uwp/api/windows.storage.storagefile.getfilefrompathasync), and
            // Unity's streamingAssetsPath contains them.
            return path.Replace('/', '\\');
#else
            return path;
#endif
        }
    }
}
