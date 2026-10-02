using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Helpers.Extensions;
using YARG.Menu.Navigation;

namespace YARG.Menu.Dialogs
{
    public class ListWithSettingsDialog : ListDialog
    {
        [Space]
        [SerializeField]
        private Transform _settingsContainer;
        [SerializeField]
        private ListToggleSetting _toggleSettingPrefab;

        private readonly List<NavigatableBehaviour> _settingNavigatables = new();

        protected override int NavigatablesBeforeList => CountLive(_settingNavigatables);

        /// <summary>
        /// Instantiates <paramref name="prefab"/> into the settings row. A setting with a
        /// <see cref="NavigatableBehaviour"/> or a <see cref="Button"/> joins the dialog's
        /// navigation before the list entries.
        /// </summary>
        public T AddSetting<T>(T prefab)
            where T : Object
        {
            var setting = Instantiate(prefab, _settingsContainer);

            AddSettingNavigatable(GetOrAttachNavigatable(setting));

            return setting;
        }

        public ListToggleSetting AddToggleSetting(string label, bool initialState, UnityAction<bool> onToggled)
        {
            var toggle = Instantiate(_toggleSettingPrefab, _settingsContainer);

            toggle.Label = label;
            toggle.Toggled = initialState;
            toggle.OnToggled.AddListener(onToggled);

            AddSettingNavigatable(RuntimeNavigatable.AttachTextHighlight(toggle.gameObject,
                () => toggle.Toggled = !toggle.Toggled));

            // Force canvas to update layout
            // TODO: this doesn't work; why?
            if (transform is RectTransform rect)
            {
                rect.ForceUpdateRectTransforms();
                LayoutRebuilder.MarkLayoutForRebuild(rect);
            }

            return toggle;
        }

        private void AddSettingNavigatable(NavigatableBehaviour navigatable)
        {
            if (navigatable == null)
            {
                return;
            }

            InsertContentNavigatable(CountLive(_settingNavigatables), navigatable);
            _settingNavigatables.Add(navigatable);
        }

        public void ClearSettings()
        {
            _settingsContainer.DestroyChildren();
            _settingNavigatables.Clear();
        }

        public override void ClearDialog()
        {
            base.ClearDialog();

            ClearList();
        }
    }
}
