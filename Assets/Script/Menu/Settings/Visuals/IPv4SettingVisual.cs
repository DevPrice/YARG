using System;
using System.Collections.Generic;
using System.Net;
using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Menu.Navigation;
using YARG.Settings.Types;

namespace YARG.Menu.Settings.Visuals
{
    public class IPv4SettingVisual : BaseSettingVisual<IPv4Setting>
    {
        private const int OCTET_COUNT = 4;

        [SerializeField]
        private TMP_InputField _inputField;

        // The octet that navigation is editing, or -1 when not editing
        private int _octetIndex = -1;

        protected override void OnEnable()
        {
            base.OnEnable();
            _inputField.textComponent.OnPreRenderText += HighlightOctet;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _inputField.textComponent.OnPreRenderText -= HighlightOctet;
        }

        public override void RefreshVisual()
        {
            _inputField.text = Setting.Value;
        }

        public override NavigationScheme GetNavigationScheme()
        {
            SelectOctet(0);

            var entries = new List<NavigationScheme.Entry>
            {
                NavigateFinish,
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Increase", () => AdjustOctet(1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Decrease", () => AdjustOctet(-1)),
                new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Previous",
                    () => SelectOctet(Math.Max(_octetIndex - 1, 0))),
                new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Next",
                    () => SelectOctet(Math.Min(_octetIndex + 1, OCTET_COUNT - 1))),
            };

            if (Setting.AllowEmpty)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Common.Clear", () =>
                {
                    Setting.Value = string.Empty;
                    RefreshVisual();
                }));
            }

            return new NavigationScheme(entries, true);
        }

        public override void OnNavigationSchemePopped()
        {
            SelectOctet(-1);
        }

        private void SelectOctet(int index)
        {
            _octetIndex = index;
            _inputField.textComponent.ForceMeshUpdate();
        }

        private void AdjustOctet(int offset)
        {
            // An empty value (allowed by some settings) starts from 0.0.0.0
            var octets = IPAddress.TryParse(Setting.Value, out var address) && IPv4Setting.IsValidIPv4(address)
                ? address.GetAddressBytes()
                : new byte[OCTET_COUNT];

            octets[_octetIndex] = (byte) Mathf.Clamp(octets[_octetIndex] + offset, byte.MinValue, byte.MaxValue);

            Setting.Value = new IPAddress(octets).ToString();
            RefreshVisual();
        }

        private void HighlightOctet(TMP_TextInfo textInfo)
        {
            if (_octetIndex < 0)
            {
                return;
            }

            var color = (Color32) RuntimeNavigatable.SelectedTextColor;

            int octet = 0;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                var character = textInfo.characterInfo[i];
                if (character.character == '.')
                {
                    octet++;
                    continue;
                }

                if (octet != _octetIndex || !character.isVisible)
                {
                    continue;
                }

                var colors = textInfo.meshInfo[character.materialReferenceIndex].colors32;
                for (int v = 0; v < 4; v++)
                {
                    colors[character.vertexIndex + v] = color;
                }
            }
        }

        public void OnTextFieldChange()
        {
            try
            {
                if (Setting.AllowEmpty && string.IsNullOrEmpty(_inputField.text))
                {
                    Setting.Value = string.Empty;
                }
                else if (IPAddress.TryParse(_inputField.text, out var ipAddress))
                {
                    if (IPv4Setting.IsValidIPv4(ipAddress))
                    {
                        Setting.Value = ipAddress.ToString();
                    }
                }
            }
            catch
            {
                // Ignore error
            }

            RefreshVisual();
        }
    }
}
