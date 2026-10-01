namespace YARG.Helpers
{
    /// <summary>
    /// Platform-dependent capabilities that settings and UI query to decide what to offer the user.
    /// A port to a new platform changes these values here instead of adding conditionals at each call site.
    /// </summary>
    public static class PlatformFeatures
    {
        /// <summary>
        /// Whether the fullscreen mode and resolution settings are offered.
        /// </summary>
        public static bool SupportsWindowModes => !IsWindowsStorePlayer;

        /// <summary>
        /// Whether <see cref="FileExplorerHelper.OpenFolder"/> and <see cref="FileExplorerHelper.OpenToFile"/>
        /// open the system file manager. Where false, they copy the path to the clipboard instead, and
        /// open-folder actions are hidden.
        /// </summary>
        public static bool CanOpenFileExplorer =>
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            true;
#else
            false;
#endif

        public static bool SupportsAsio => IsWindowsDesktop;

        public static bool SupportsWasapi => IsWindowsDesktop;

        /// <summary>
        /// Whether Discord rich presence is available.
        /// </summary>
        public static bool SupportsDiscord => !IsWindowsStorePlayer;

        /// <summary>
        /// Whether launch arguments such as <c>-offline</c> or <c>-persistent-data-path</c> are read.
        /// </summary>
        public static bool SupportsCommandLineArgs => !IsWindowsStorePlayer;

        /// <summary>
        /// Whether adding an XInput device asks which kind of controller it is (gamepad, CRKD guitar,
        /// RB4InstrumentMapper, ...) before applying default bindings.
        /// </summary>
        public static bool SupportsXInputGamepadModePrompt => IsWindowsDesktop || IsWindowsStorePlayer;

        /// <summary>
        /// Whether .yarground venue bundles can be loaded. They are built for the desktop players only.
        /// </summary>
        public static bool SupportsYargroundBundles => !IsWindowsStorePlayer;

        // Unlike UNITY_STANDALONE_WIN alone, this excludes a non-Windows editor targeting Windows,
        // where the Windows-only native libraries are not loaded.
        private static bool IsWindowsDesktop =>
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            true;
#else
            false;
#endif

        private static bool IsWindowsStorePlayer =>
#if UNITY_WSA && !UNITY_EDITOR
            true;
#else
            false;
#endif
    }
}
