using System.Globalization;
using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Menu.Navigation;
using YARG.Settings.Types;

namespace YARG.Menu.Settings.Visuals
{
    public class DMXChannelsSettingVisual : BaseSettingVisual<DMXChannelsSetting>
    {
        [SerializeField]
        private TMP_InputField[] _inputField;

        private InputFieldCursor _cursor;

        public override void RefreshVisual()
        {
            for (int i = 0; i < Setting.Value.Length; i++)
            {
                _inputField[i].text = Setting.Value[i].ToString(CultureInfo.InvariantCulture);
            }
        }

        public override NavigationScheme GetNavigationScheme()
        {
            _cursor?.Clear();
            _cursor = new InputFieldCursor(_inputField);

            return new NavigationScheme(new()
            {
                NavigateFinish,
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Increase", () => AdjustChannel(1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Decrease", () => AdjustChannel(-1)),
                new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Previous", () => _cursor.Move(-1)),
                new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Next", () => _cursor.Move(1)),
            }, true);
        }

        public override void OnNavigationSchemePopped()
        {
            _cursor?.Clear();
            _cursor = null;
        }

        private void AdjustChannel(int offset)
        {
            int index = _cursor.Index;
            if (index >= Setting.Value.Length)
            {
                return;
            }

            Setting.Value[index] = Mathf.Clamp(Setting.Value[index] + offset, Setting.Min, Setting.Max);
            RefreshVisual();
        }

        public void OnTextFieldChange(int index)
        {

            try
            {
                int value = int.Parse(_inputField[index].text, CultureInfo.InvariantCulture);
                value = Mathf.Clamp(value, Setting.Min, Setting.Max);
                Setting.Value[index] = value;
            }
            catch
            {
                // Ignore error
            }

            RefreshVisual();
        }
    }
}
