using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Settings.Types;

namespace YARG.Menu.Settings.Visuals
{
    public class FileInfoSettingVisual : BaseSettingVisual<FileInfoSetting>
    {
        [SerializeField]
        private TextMeshProUGUI _statusText;

        [SerializeField]
        private Image _removeButton;

        public override NavigationScheme GetNavigationScheme()
        {
            return new NavigationScheme(new()
            {
                NavigateFinish,
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Browse", Browse),
                new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Common.Remove", Remove),
            }, true);
        }

        private Color _enabledButtonColor;
        private readonly Color _disabledButtonColor = Color.gray;

        protected override void OnSettingInit()
        {
            _enabledButtonColor = _removeButton.color;
            RefreshVisual();
        }

        public override void RefreshVisual()
        {
            _removeButton.color = Setting.Value != null ? _enabledButtonColor : _disabledButtonColor;
            var key = Setting.Value == null ? "Menu.Common.Disabled" : "Menu.Common.Enabled";
            _statusText.text = Localize.Key(key);
        }

        public void Remove()
        {
            Setting.Value = null;
            RefreshVisual();

            // I am not sure why this is not being invoked by the setter
            Setting.OnChange?.Invoke(Setting.Value);
        }

        public void Browse()
        {
            FileExplorerHelper.OpenChooseFile("", "png", file =>
            {
                Setting.Value = new FileInfo(file);
                RefreshVisual();
            });
        }
    }
}
