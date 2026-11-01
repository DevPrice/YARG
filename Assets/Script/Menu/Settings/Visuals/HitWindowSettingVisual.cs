using System;
using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Menu.Navigation;
using YARG.Settings.Types;

namespace YARG.Menu.Settings.Visuals
{
    public class HitWindowSettingVisual : BaseSettingVisual<HitWindowSetting>
    {
        [SerializeField]
        private GameObject _dynamicContainer;
        [SerializeField]
        private GameObject _constantContainer;

        [Space]
        [SerializeField]
        private DurationInputField _minField;
        [SerializeField]
        private DurationInputField _maxField;

        [Space]
        [SerializeField]
        private DurationInputField _constantField;

        private InputFieldCursor _cursor;

        protected override void OnSettingInit()
        {
            _minField.PreferredUnit = DurationInputField.Unit.Milliseconds;
            _maxField.PreferredUnit = DurationInputField.Unit.Milliseconds;
            _constantField.PreferredUnit = DurationInputField.Unit.Milliseconds;

            base.OnSettingInit();
        }

        public override void RefreshVisual()
        {
            if (Setting.Value.IsDynamic)
            {
                _dynamicContainer.SetActive(true);
                _constantContainer.SetActive(false);

                _minField.Duration = Setting.Value.MinWindow;
                _maxField.Duration = Setting.Value.MaxWindow;
            }
            else
            {
                _dynamicContainer.SetActive(false);
                _constantContainer.SetActive(true);

                // We should use max here because that's what the
                // hit window snaps to when using a constant size.
                _constantField.Duration = Setting.Value.MaxWindow;
            }
        }

        public override NavigationScheme GetNavigationScheme()
        {
            _cursor?.Clear();
            _cursor = Setting.Value.IsDynamic
                ? new InputFieldCursor(_minField.GetComponent<TMP_InputField>(),
                    _maxField.GetComponent<TMP_InputField>())
                : new InputFieldCursor(_constantField.GetComponent<TMP_InputField>());

            return new NavigationScheme(new()
            {
                NavigateFinish,
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Increase", () => AdjustWindow(1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Decrease", () => AdjustWindow(-1)),
                new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Previous", () => _cursor.Move(-1)),
                new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Next", () => _cursor.Move(1)),
            }, true);
        }

        public override void OnNavigationSchemePopped()
        {
            _cursor?.Clear();
            _cursor = null;
        }

        private void AdjustWindow(int milliseconds)
        {
            double step = milliseconds * DurationInputField.GetMultiplierForUnit(DurationInputField.Unit.Milliseconds);

            if (!Setting.Value.IsDynamic)
            {
                _constantField.Duration += step;
            }
            else if (_cursor.Index == 0)
            {
                _minField.Duration = Math.Min(_minField.Duration + step, _maxField.Duration);
            }
            else
            {
                _maxField.Duration = Math.Max(_maxField.Duration + step, _minField.Duration);
            }

            OnTextFieldChange();
        }

        public void OnTextFieldChange()
        {
            var window = Setting.Value;

            if (Setting.Value.IsDynamic)
            {
                window.MinWindow = _minField.Duration;
                window.MaxWindow = _maxField.Duration;
            }
            else
            {
                // We should use both here though to prevent the minimum from being higher than the max
                window.MinWindow = _constantField.Duration;
                window.MaxWindow = _constantField.Duration;
            }

            // Since the hit window is a reference type, we can just do this
            Setting.ForceInvokeCallback();
        }
    }
}
