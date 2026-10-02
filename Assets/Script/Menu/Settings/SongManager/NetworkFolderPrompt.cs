using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using YARG.Localization;
using YARG.Menu.Persistent;
using YARG.Settings;
using YARG.Song;
using YARG.Song.Network;

namespace YARG.Menu.Settings
{
    /// <summary>
    /// Asks for a \\server\share[\folder] path, tests the connection off the main thread, and saves the folder
    /// (then rescans) only when the test passes.
    /// </summary>
    public static class NetworkFolderPrompt
    {
        private const string DIALOG_KEY = "Menu.Dialog.NetworkFolder";

        private static List<string> NetworkFolders => SettingsManager.Settings.NetworkSongFolders;

        private static bool _testing;

        public static void Add()
        {
            Show(-1);
        }

        /// <param name="index">Index into the network song folders of the entry to replace</param>
        public static void Edit(int index)
        {
            Show(index);
        }

        private static void Show(int index)
        {
            if (_testing || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            string title = Localize.Key(DIALOG_KEY, index < 0 ? "Title" : "EditTitle");
            var dialog = DialogManager.Instance.ShowRenameDialog(title, input => SubmitAsync(index, title, input).Forget());
            dialog.SetInputText(index < 0 ? @"\\" : NetworkFolders[index]);
        }

        private static async UniTask SubmitAsync(int index, string title, string input)
        {
            // The rename dialog is still showing while it runs this callback, and only one dialog may exist.
            await UniTask.NextFrame();

            if (!SmbPath.TryParse(input.Trim(), out var path))
            {
                ShowFailure(Localize.KeyFormat((DIALOG_KEY, "InvalidPath"), input.Trim()));
                return;
            }

            // Store the canonical spelling: TryParse accepts extra separators (the dialog is prefilled with \\,
            // so typing a full path gives \\\\server\share), but the scanner and the SMB mount compare root
            // strings, and a non-canonical root falls through to the OS file system and fails.
            string folder = path.ToString();

            int existing = NetworkFolders.FindIndex(other =>
                string.Equals(other.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && existing != index)
            {
                ShowFailure(Localize.KeyFormat((DIALOG_KEY, "Duplicate"), folder));
                return;
            }

            if (DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            NetworkFolderCheck check;
            _testing = true;
            try
            {
                var progress = DialogManager.Instance.ShowMessage(title,
                    Localize.KeyFormat((DIALOG_KEY, "Testing"), folder));
                check = await UniTask.RunOnThreadPool(() => SmbNetworkFolders.Check(folder));
                if (progress.IsOpen)
                {
                    DialogManager.Instance.ClearDialog();
                }
            }
            finally
            {
                _testing = false;
            }

            if (check.Status != NetworkFolderStatus.Online)
            {
                ShowFailure(Localize.KeyFormat((DIALOG_KEY, "Failed", check.Status), check.Detail));
                return;
            }

            if (index >= 0 && index < NetworkFolders.Count)
            {
                NetworkFolders[index] = folder;
            }
            else
            {
                NetworkFolders.Add(folder);
            }

            // Save now rather than when the settings menu closes: a first scan of a large share can run long
            // enough for the app to be closed mid-scan, which would otherwise lose the folder.
            SettingsManager.SaveSettings();
            ToastManager.ToastSuccess(Localize.KeyFormat((DIALOG_KEY, "Connected"), folder));

            using (var context = new LoadingContext())
            {
                await SongContainer.RunRefresh(false, context);
            }

            if (SettingsMenu.Instance != null)
            {
                SettingsMenu.Instance.RefreshAndKeepPosition();
            }
        }

        private static void ShowFailure(string message)
        {
            if (DialogManager.Instance.IsDialogShowing)
            {
                ToastManager.ToastError(message);
                return;
            }

            DialogManager.Instance.ShowMessage(Localize.Key(DIALOG_KEY, "Failed", "Title"), message);
        }
    }
}
