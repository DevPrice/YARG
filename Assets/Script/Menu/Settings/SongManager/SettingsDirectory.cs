using System.Collections.Generic;
using TMPro;
using UnityEngine;
using YARG.Helpers;
using YARG.Localization;
using YARG.Settings;
using YARG.Song;
using YARG.Song.Network;

namespace YARG.Menu.Settings
{
    public class SettingsDirectory : MonoBehaviour
    {
        private List<string> Folders => _isNetwork
            ? SettingsManager.Settings.NetworkSongFolders
            : SettingsManager.Settings.SongFolders;

        [SerializeField]
        private TextMeshProUGUI _pathText;
        [SerializeField]
        private TextMeshProUGUI _songCountText;

        private int _index;
        private bool _isNetwork;

        public void SetIndex(int index)
        {
            _index = index;
            _isNetwork = false;
            RefreshText();
        }

        public void SetNetworkIndex(int index)
        {
            _index = index;
            _isNetwork = true;
            RefreshText();
        }

        private void RefreshText()
        {
            string folder = Folders[_index];
            if (string.IsNullOrEmpty(folder))
            {
                _pathText.text = Localize.Key("Menu.Settings.NoFolder");
                _songCountText.text = string.Empty;
            }
            else
            {
                _pathText.text = _isNetwork
                    ? Localize.KeyFormat("Menu.Settings.NetworkFolder", folder)
                    : folder;

                int songCount = 0;
                foreach (var song in SongContainer.UnfilteredSongs)
                {
                    if (song.SortBasedLocation.StartsWith(folder))
                    {
                        songCount++;
                    }
                }

                if (_isNetwork && SmbNetworkFolders.IsOffline(folder))
                {
                    _songCountText.text = Localize.Key("Menu.Settings.NetworkFolderOffline");
                }
                else if (songCount == 0)
                {
                    _songCountText.text = Localize.Key("Menu.Settings.ScanNeeded");
                }
                else
                {
                    _songCountText.text = Localize.KeyFormat("Menu.Settings.SongCount", songCount);
                }
            }
        }

        private void Awake()
        {
            SongManagerHeader.AddButtonsToNavigation(gameObject);
        }

        public void Remove()
        {
            // Remove the element
            Folders.RemoveAt(_index);

            // Refresh
            SettingsMenu.Instance.Refresh();
        }

        public void Browse()
        {
            if (_isNetwork)
            {
                NetworkFolderPrompt.Edit(_index);
                return;
            }

            var startingDir = Folders[_index];
            FileExplorerHelper.OpenChooseFolder(startingDir, folder =>
            {
                Folders[_index] = folder;
                RefreshText();
            });
        }
    }
}
