using System;
using System.IO;
using System.Runtime.InteropServices;

namespace YARG.Helpers
{
    /// <summary>
    ///     Where each platform keeps native plugins, and how to load one by hand. Supporting a new platform for
    ///     the native libraries means adding a case here, not in each binding.
    /// </summary>
    internal static class NativeLibraries
    {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private const int RTLD_NOW = 2;
        private const int RTLD_GLOBAL = 8;
#endif

        /// <summary>
        ///     The folder holding native plugins: in the editor, the platform folder of
        ///     <c>Assets/Plugins/<paramref name="editorFolder"/></c>; in a player, where the build copied them.
        /// </summary>
        /// <param name="dataPath">The <c>Assets</c> folder in the editor, or the player's data folder.</param>
        public static string GetPluginDirectory(string dataPath, string editorFolder)
        {
            string directory = Path.Combine(dataPath, "Plugins");
#if UNITY_EDITOR_OSX
            return Path.Combine(directory, editorFolder, "Mac");
#elif UNITY_EDITOR_LINUX
            return Path.Combine(directory, editorFolder, "Linux", "x86_64");
#elif UNITY_EDITOR
            return Path.Combine(directory, editorFolder, "Windows", "x86_64");
#elif UNITY_STANDALONE_WIN && UNITY_64
            return Path.Combine(directory, "x86_64");
#elif UNITY_STANDALONE_WIN
            return Path.Combine(directory, "x86");
#else
            return directory;
#endif
        }

        /// <summary>
        ///     The path to pass to <see cref="Load"/> for the library <paramref name="name"/> (no prefix or
        ///     extension). On platforms without a known plugin layout, this is the bare name.
        /// </summary>
        public static string GetLibraryPath(string dataPath, string editorFolder, string name)
        {
#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
            return Path.Combine(GetPluginDirectory(dataPath, editorFolder), $"lib{name}.dylib");
#elif UNITY_EDITOR_LINUX || (!UNITY_EDITOR && UNITY_STANDALONE_LINUX)
            return Path.Combine(GetPluginDirectory(dataPath, editorFolder), $"lib{name}.so");
#elif UNITY_EDITOR || UNITY_STANDALONE_WIN
            return Path.Combine(GetPluginDirectory(dataPath, editorFolder), $"{name}.dll");
#else
            return name;
#endif
        }

        /// <returns>The library's handle, or <see cref="IntPtr.Zero"/> if it could not be loaded.</returns>
        public static IntPtr Load(string path)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return WindowsNative.LoadLibrary(path);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return LinuxNative.dlopen(path, RTLD_NOW | RTLD_GLOBAL);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return MacNative.dlopen(path, RTLD_NOW | RTLD_GLOBAL);
#else
            return IntPtr.Zero;
#endif
        }

        /// <returns>The address of <paramref name="symbol"/>, or <see cref="IntPtr.Zero"/> if it isn't exported.</returns>
        public static IntPtr GetExport(IntPtr handle, string symbol)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return WindowsNative.GetProcAddress(handle, symbol);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return LinuxNative.dlsym(handle, symbol);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return MacNative.dlsym(handle, symbol);
#else
            return IntPtr.Zero;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static class WindowsNative
        {
            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        }
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        private static class LinuxNative
        {
            [DllImport("libdl.so.2")]
            public static extern IntPtr dlopen(string filename, int flags);

            [DllImport("libdl.so.2")]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private static class MacNative
        {
            [DllImport("libSystem.dylib")]
            public static extern IntPtr dlopen(string filename, int flags);

            [DllImport("libSystem.dylib")]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
        }
#endif
    }
}
