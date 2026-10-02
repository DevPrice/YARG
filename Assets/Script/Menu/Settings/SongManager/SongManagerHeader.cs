using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song.Cache;
using YARG.Helpers;
using YARG.Menu.Navigation;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Settings
{
    public class SongManagerHeader : MonoBehaviour
    {
        [SerializeField]
        private ColoredButton _badSongsButton;

        private void Awake()
        {
            CheckBadSongsFile();
            AddButtonsToNavigation(gameObject);
        }

        /// <summary>
        /// Adds every button under <paramref name="root"/> to the settings navigation group, left to right.
        /// Call it while the tab is being built (Awake of a freshly instantiated row) so the buttons land between
        /// the rows spawned before and after it.
        /// </summary>
        internal static void AddButtonsToNavigation(GameObject root)
        {
            var navGroup = SettingsMenu.Instance.SettingsNavGroup;

            // The prefabs list their right-anchored buttons right to left
            var buttons = root.GetComponentsInChildren<Button>(true);
            Array.Sort(buttons, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

            foreach (var button in buttons)
            {
                navGroup.AddNavigatable(RuntimeNavigatable.Attach(button.gameObject, () =>
                {
                    if (button.interactable)
                    {
                        button.onClick.Invoke();
                    }
                }));
            }
        }

        public void AddNewFolder()
        {
            SettingsManager.Settings.SongFolders.Add(string.Empty);
            SettingsMenu.Instance.RefreshAndKeepPosition();
        }

        public async void RefreshSongs()
        {
            using var context = new LoadingContext();
            await SongContainer.RunRefresh(false, context);
            SettingsMenu.Instance.RefreshAndKeepPosition();
        }

        private void CheckBadSongsFile()
        {
            _badSongsButton.gameObject.SetActive(File.Exists(PathHelper.BadSongsPath));
            
            var numErrors = CacheHandler.Progress.BadSongCount;

            if (numErrors > 0)
            {
                var errors = numErrors == 1 ? "ERROR" : "ERRORS";
                _badSongsButton.Text.text = $"{numErrors} {errors} FOUND";
            }
        }

        public void OpenBadSongs()
        {
            FileExplorerHelper.OpenFolder(PathHelper.BadSongsPath);
        }
    }
}