using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Navigation;

namespace YARG.Menu.Dialogs
{
    public class OneTimeMessageDialog : MessageDialog
    {
        [SerializeField]
        private Toggle _toggle;

        public Action DontShowAgainAction;

        private RuntimeNavigatable _toggleNavigatable;

        /// <summary>
        /// Adds the "don't show again" toggle to the dialog's navigation, after the buttons so that the
        /// first confirm still selects a button. Call this after the buttons are added, since adding
        /// them clears the navigation group.
        /// </summary>
        public void AddToggleNavigation()
        {
            if (_toggleNavigatable == null)
            {
                // The label is outside the Toggle prefab instance, so highlight the row that holds both
                var row = _toggle.transform;
                while (row.parent != null && row.GetComponentInChildren<TextMeshProUGUI>(true) == null)
                {
                    row = row.parent;
                }

                _toggleNavigatable = RuntimeNavigatable.AttachTextHighlight(row.gameObject,
                    () => _toggle.isOn = !_toggle.isOn);
            }

            NavigationGroup.AddNavigatable(_toggleNavigatable);
        }

        protected override void OnBeforeClose()
        {
            if (_toggle.isOn)
            {
                DontShowAgainAction?.Invoke();
            }
        }
    }
}