using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Filters;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.ProfileInfo;
using YARG.Player;
using YARG.Scores;
using YARG.Settings.Customization;

namespace YARG.Menu.ProfileList
{
    // This will be cleaned up when we add the new profile overview screen

    public class ProfileSidebar : MonoBehaviour
    {
        private const string NUMBER_FORMAT = "0.0###";

        private const float NOTE_SPEED_STEP     = 0.5f;
        private const float HIGHWAY_LENGTH_STEP = 0.1f;

        private static readonly GameMode[] _gameModes =
        {
            GameMode.FiveFretGuitar,
            GameMode.SixFretGuitar,
            GameMode.EliteDrums,
            GameMode.FourLaneDrums,
            GameMode.FiveLaneDrums,
            GameMode.Vocals,
            GameMode.ProKeys
        };

        private static readonly StarPowerActivationType[] _starPowerActivationTypes =
        {
            StarPowerActivationType.RightmostNote,
            StarPowerActivationType.AllNotes,
        };

        private static readonly OpenLaneDisplayType[] _openLaneDisplayTypes =
        {
            OpenLaneDisplayType.Never,
            OpenLaneDisplayType.IfChartContainsOpens,
            OpenLaneDisplayType.Always,
        };

        [SerializeField]
        private GameObject _contents;
        [SerializeField]
        private TextMeshProUGUI _profileName;
        [SerializeField]
        private TMP_InputField _nameInput;
        [SerializeField]
        private Image _profilePicture;
        [SerializeField]
        private Button[] _profileActionButtons;

        [Space]
        [SerializeField]
        private GameObject _sidebarContent;
        [SerializeField]
        private TMP_Dropdown _gameModeDropdown;
        [SerializeField]
        private TMP_InputField _noteSpeedField;
        [SerializeField]
        private TMP_InputField _highwayLengthField;
        [SerializeField]
        private TMP_InputField _inputCalibrationField;
        [SerializeField]
        private Toggle _leftyFlipToggle;
        [SerializeField]
        private Toggle _rangeDisabledToggle;
        [SerializeField]
        private TMP_Dropdown _openLaneDisplayTypeDropdown;
        [SerializeField]
        private Toggle _useCymbalModelsToggle;
        [SerializeField]
        private TMP_Dropdown _starPowerActivationTypeDropdown;
        [SerializeField]
        private TMP_Dropdown _engineDropdown;
        [SerializeField]
        private TMP_Dropdown _themeDropdown;
        [SerializeField]
        private TMP_Dropdown _colorProfileDropdown;
        [SerializeField]
        private TMP_Dropdown _cameraPresetDropdown;
        [SerializeField]
        private TMP_Dropdown _highwayPresetDropdown;
        [SerializeField]
        private TMP_Dropdown _rockMeterPresetDropdown;

        [Space]
        [SerializeField]
        private GameObject _nameContainer;
        [SerializeField]
        private GameObject _editNameContainer;

        [Space]
        [SerializeField]
        private ProfileListMenu _profileListMenu;

        [Space]
        [SerializeField]
        private Sprite _profileGenericSprite;
        [SerializeField]
        private Sprite _profileBotSprite;

        private ProfileView _profileView;
        private YargProfile _profile;

        private readonly List<GameMode> _gameModesByIndex = new();
        private readonly List<OpenLaneDisplayType> _openLaneDisplayTypesByIndex = new();
        private readonly List<StarPowerActivationType> _starPowerActivationTypesByIndex = new();

        private List<Guid> _enginePresetsByIndex;
        private List<Guid> _colorProfilesByIndex;
        private List<Guid> _cameraPresetsByIndex;
        private List<Guid> _themesByIndex;
        private List<Guid> _highwayPresetsByIndex;
        private List<Guid> _rockmeterPresetsByIndex;

        private NavigationGroup _navigationGroup;
        private ScrollRect _settingsScrollRect;
        private TMP_Dropdown[] _dropdowns;

        private TMP_InputField _steppedField;
        private Color _steppedFieldColor;
        private bool _steppingField;

        /// <summary>
        /// Whether menu navigation is moving through the sidebar rather than the profile list.
        /// </summary>
        public bool IsNavigating { get; private set; }

        /// <summary>
        /// Whether one of the sidebar's dropdown lists is open, which puts the list's own navigation scheme on top.
        /// </summary>
        public bool IsDropdownListOpen
        {
            get
            {
                if (_dropdowns == null)
                {
                    return false;
                }

                foreach (var dropdown in _dropdowns)
                {
                    if (dropdown.transform.Find("Dropdown List") != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void Awake()
        {
            // Setup dropdown items
            _gameModeDropdown.options.Clear();
            foreach (var gameMode in _gameModes)
            {
                _gameModesByIndex.Add(gameMode);

                // Create the dropdown option
                _gameModeDropdown.options.Add(new(gameMode.ToLocalizedName()));
            }

            InitializeNavigation();
        }

        private void OnEnable()
        {
            // These things can change, so do it every time it's enabled.

            PopulateDropdownOptions();
        }

        private void OnDisable()
        {
            ExitNavigation();
        }

        private void InitializeNavigation()
        {
            _navigationGroup = _contents.AddComponent<NavigationGroup>();
            _navigationGroup.SelectionChanged += OnNavigationSelectionChanged;

            _settingsScrollRect = _sidebarContent.GetComponentInParent<ScrollRect>(true);
            _dropdowns = _contents.GetComponentsInChildren<TMP_Dropdown>(true);

            // In visual order: the name in the header, the setting rows, then the action buttons below them
            AddNavigatable(_nameContainer, () => SetNameEditMode(true));

            foreach (Transform row in _sidebarContent.transform)
            {
                AddRowNavigation(row);
            }

            foreach (var button in _profileActionButtons)
            {
                var captured = button;
                AddNavigatable(button.gameObject, () => ClickIfInteractable(captured));
            }
        }

        private void AddRowNavigation(Transform row)
        {
            // Checked before toggles because a dropdown's item template contains one
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown != null)
            {
                AddNavigatable(row.gameObject, () =>
                {
                    if (dropdown.interactable)
                    {
                        RuntimeNavigatable.OpenDropdownList(dropdown);
                    }
                });
                return;
            }

            var toggle = row.GetComponentInChildren<Toggle>(true);
            if (toggle != null)
            {
                AddNavigatable(row.gameObject, () =>
                {
                    if (toggle.interactable)
                    {
                        toggle.isOn = !toggle.isOn;
                    }
                });
                return;
            }

            // The note speed and highway length fields share a row, so each field's half is its own entry
            var fields = row.GetComponentsInChildren<TMP_InputField>(true);
            if (fields.Length > 0)
            {
                foreach (var field in fields)
                {
                    var target = fields.Length > 1 ? GetChildContaining(row, field.transform) : row;
                    AddNavigatable(target.gameObject, () => StartSteppingField(field));
                }
                return;
            }

            var button = row.GetComponentInChildren<Button>(true);
            if (button != null)
            {
                AddNavigatable(row.gameObject, () => ClickIfInteractable(button));
            }
        }

        private void AddNavigatable(GameObject target, Action confirm)
        {
            var nav = RuntimeNavigatable.Attach(target, confirm);

            // Tint the row's label like the settings menu does, on top of the row's own highlight
            var label = target.transform.Find("Option Name")?.GetComponent<TextMeshProUGUI>();
            if (label != null)
            {
                var defaultColor = label.color;
                var baseVisual = nav.SelectionVisual;
                nav.SelectionVisual = selected =>
                {
                    baseVisual?.Invoke(selected);

                    var color = RuntimeNavigatable.SelectedTextColor;
                    color.a = defaultColor.a;
                    label.color = selected ? color : defaultColor;
                };
            }

            _navigationGroup.AddNavigatable(nav);
        }

        private static Transform GetChildContaining(Transform parent, Transform descendant)
        {
            var current = descendant;
            while (current.parent != null && current.parent != parent)
            {
                current = current.parent;
            }

            return current;
        }

        private static void ClickIfInteractable(Button button)
        {
            if (button.interactable)
            {
                button.onClick.Invoke();
            }
        }

        /// <summary>
        /// Moves menu navigation from the profile list into the sidebar. Back returns to the list.
        /// </summary>
        public void EnterNavigation()
        {
            if (IsNavigating || !_contents.activeSelf)
            {
                return;
            }

            IsNavigating = true;

            Navigator.Instance.PushSchemeImmediate(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up",
                    ctx => _navigationGroup.SelectPrevious(ctx.IsRepeat)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down",
                    ctx => _navigationGroup.SelectNext(ctx.IsRepeat)),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm",
                    () => _navigationGroup.ConfirmSelection()),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", ExitNavigation),
            }, true));

            _settingsScrollRect.verticalNormalizedPosition = 1f;
            _navigationGroup.SelectFirst(SelectionOrigin.Navigation);
        }

        /// <summary>
        /// Returns menu navigation to the profile list. Does nothing if the sidebar isn't being navigated.
        /// </summary>
        public void ExitNavigation()
        {
            if (!IsNavigating)
            {
                return;
            }

            StopSteppingField();

            IsNavigating = false;
            _navigationGroup.ClearSelection();

            if (Navigator.Instance != null)
            {
                Navigator.Instance.PopScheme();
            }
        }

        private void OnNavigationSelectionChanged(NavigatableBehaviour selected, SelectionOrigin selectionOrigin)
        {
            // Stepping applies to the field it started on; a click elsewhere ends it
            StopSteppingField();

            if (selectionOrigin == SelectionOrigin.Navigation && selected != null &&
                selected.transform.IsChildOf(_sidebarContent.transform))
            {
                _settingsScrollRect.ScrollIntoView((RectTransform) selected.transform);
            }
        }

        /// <summary>
        /// Lets Up and Down step a numeric field, since a controller can't type into it.
        /// </summary>
        private void StartSteppingField(TMP_InputField field)
        {
            if (!field.interactable)
            {
                return;
            }

            StopSteppingField();

            _steppingField = true;
            _steppedField = field;
            _steppedFieldColor = field.textComponent.color;
            field.textComponent.color = RuntimeNavigatable.SelectedTextColor;

            Navigator.Instance.PushSchemeImmediate(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Increase", () => StepField(field, 1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Decrease", () => StepField(field, -1)),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", StopSteppingField),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", StopSteppingField),
            }, null));
        }

        private void StopSteppingField()
        {
            if (!_steppingField)
            {
                return;
            }

            _steppingField = false;
            if (_steppedField != null)
            {
                _steppedField.textComponent.color = _steppedFieldColor;
            }
            _steppedField = null;

            if (Navigator.Instance != null)
            {
                Navigator.Instance.PopScheme();
            }
        }

        private void StepField(TMP_InputField field, int direction)
        {
            float.TryParse(field.text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value);

            float step = field == _noteSpeedField ? NOTE_SPEED_STEP
                : field == _highwayLengthField ? HIGHWAY_LENGTH_STEP
                : 1f;

            // The field's end-edit handler parses, clamps and reformats the value, as it does after typing
            field.text = (value + direction * step).ToString(CultureInfo.CurrentCulture);
            field.onEndEdit.Invoke(field.text);
        }

        private void PopulateDropdownOptions()
        {
            // Setup preset dropdowns
            _enginePresetsByIndex =
                CustomContentManager.EnginePresets.AddOptionsToDropdown(_engineDropdown)
                    .Select(i => i.Id).ToList();
            _themesByIndex =
                CustomContentManager.ThemePresets.AddOptionsToDropdown(_themeDropdown)
                    .Select(i => i.Id).ToList();
            _colorProfilesByIndex =
                CustomContentManager.ColorProfiles.AddOptionsToDropdown(_colorProfileDropdown)
                    .Select(i => i.Id).ToList();
            _cameraPresetsByIndex =
                CustomContentManager.CameraSettings.AddOptionsToDropdown(_cameraPresetDropdown)
                    .Select(i => i.Id).ToList();
            _highwayPresetsByIndex =
                CustomContentManager.HighwayPresets.AddOptionsToDropdown(_highwayPresetDropdown)
                    .Select(i => i.Id).ToList();
            _rockmeterPresetsByIndex =
                CustomContentManager.RockMeterPresets.AddOptionsToDropdown(_rockMeterPresetDropdown)
                    .Select(i => i.Id).ToList();

            // Set drum star power activation type
            _starPowerActivationTypeDropdown.options.Clear();
            foreach (var starPowerActivationType in _starPowerActivationTypes)
            {
                _starPowerActivationTypesByIndex.Add(starPowerActivationType);
                _starPowerActivationTypeDropdown.options.Add(new(starPowerActivationType.ToLocalizedName()));
            }

            _openLaneDisplayTypeDropdown.options.Clear();
            foreach (var openLaneDisplayType in _openLaneDisplayTypes)
            {
                _openLaneDisplayTypesByIndex.Add(openLaneDisplayType);
                _openLaneDisplayTypeDropdown.options.Add(new(openLaneDisplayType.ToLocalizedName()));
            }
        }

        private void RemoveUnusedDropdownOptions(YargProfile profile)
        {
            // TODO: Refactor presets so that this doesn't have to be so tightly coupled to the preset implementation
            //  We could use reflection to figure out what each alternate default changes and only show ones that
            //  change something relevant to the profile's game mode

            // Solo Taps only changes FiveFretGuitar
            if (profile.GameMode is not GameMode.FiveFretGuitar)
            {
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.SoloTaps.Id);
            }

            // Casual only changes FiveFretGuitar, SixFretGuitar, and Vocals
            if (profile.GameMode is not (GameMode.FiveFretGuitar or GameMode.Vocals or GameMode.SixFretGuitar))
            {
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.Casual.Id);
            }

            // Pro keys isn't changed by anything, apparently
            if (profile.GameMode is GameMode.ProKeys)
            {
                // We will have necessarily already removed SoloTaps and Casual, so removing Precision removes all but Default
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.Precision.Id);
            }
        }

        private void RemoveDropdownOption(TMP_Dropdown dropdown, List<Guid> presetsByIndex, Guid guid)
        {
            for (int i = presetsByIndex.Count - 1; i >= 0; i--)
            {
                if (presetsByIndex[i] == guid)
                {
                    dropdown.options.RemoveAt(i);
                    presetsByIndex.RemoveAt(i);
                    break;
                }
            }
        }

        public void UpdateSidebar(YargProfile profile, ProfileView profileView)
        {
            _profile = profile;
            _profileView = profileView;

            StopSteppingField();
            if (!IsNavigating && _navigationGroup != null)
            {
                // A mouse click can select a row without entering sidebar navigation
                _navigationGroup.ClearSelection();
            }

            if (!PlayerContainer.IsProfileTaken(_profile))
            {
                HideContents();
                return;
            }

            PopulateDropdownOptions();
            RemoveUnusedDropdownOptions(profile);

            _contents.SetActive(true);

            // Display the profile's options
            _profileName.text = _profile.Name;
            _gameModeDropdown.value = _gameModesByIndex.IndexOf(profile.GameMode);
            _starPowerActivationTypeDropdown.value = _starPowerActivationTypesByIndex
                .IndexOf(profile.StarPowerActivationType);
            _noteSpeedField.text = profile.NoteSpeed.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
            _highwayLengthField.text = profile.HighwayLength.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
            _inputCalibrationField.text = _profile.InputCalibrationMilliseconds.ToString();
            _leftyFlipToggle.isOn = profile.LeftyFlip;
            _rangeDisabledToggle.isOn = profile.RangeEnabled;
            _openLaneDisplayTypeDropdown.value = _openLaneDisplayTypesByIndex.IndexOf(profile.OpenLaneDisplayType);
            _useCymbalModelsToggle.isOn = profile.UseCymbalModels;

            // Update preset dropdowns
            _engineDropdown.SetValueWithoutNotify(
                _enginePresetsByIndex.IndexOf(profile.EnginePreset));
            _themeDropdown.SetValueWithoutNotify(
                _themesByIndex.IndexOf(profile.ThemePreset));
            _colorProfileDropdown.SetValueWithoutNotify(
                _colorProfilesByIndex.IndexOf(profile.ColorProfile));
            _cameraPresetDropdown.SetValueWithoutNotify(
                _cameraPresetsByIndex.IndexOf(profile.CameraPreset));
            _highwayPresetDropdown.SetValueWithoutNotify(
                _highwayPresetsByIndex.IndexOf(profile.HighwayPreset));
            _openLaneDisplayTypeDropdown.SetValueWithoutNotify(
                _openLaneDisplayTypesByIndex.IndexOf(profile.OpenLaneDisplayType));
            _starPowerActivationTypeDropdown.SetValueWithoutNotify(
                _starPowerActivationTypesByIndex.IndexOf(profile.StarPowerActivationType));
            _rockMeterPresetDropdown.SetValueWithoutNotify(
                _rockmeterPresetsByIndex.IndexOf(profile.RockMeterPreset));

            // Not all game modes support all engine presets.
            // If the current engine doesn't exist for the selected instrument, the above _engineDropdown
            // will be silently set to index 0, but the engine itself will not have been set, so we need
            // to explicitly set it.
            ChangeEngine();

            // Show the proper name container (hide the editing version)
            _nameContainer.SetActive(true);
            _editNameContainer.SetActive(false);

            // Display the proper profile picture
            _profilePicture.sprite = profile.IsBot ? _profileBotSprite : _profileGenericSprite;

            // Enable/disable the edit profile button
            bool interactable = !_profile.IsBot && PlayerContainer.IsProfileTaken(_profile);
            foreach (var button in _profileActionButtons)
            {
                button.interactable = interactable;
            }

            EnableSettingsForGameMode();
        }

        private void EnableSettingsForGameMode()
        {
            // The passed dictionary is empty because we don't currently have any conditionalized profile settings (we used to, but they've all been
            // superseded by the highway ordering interface). You can still populate this dictionary to conditionalize certain settings behind certain
            // values of other settings ("hide setting X if setting Y has value Z", etc.).
            var possibleSettings = _profile.GameMode.PossibleProfileSettings(new());

            for (var i = 0; i < _sidebarContent.transform.childCount; i++)
            {
                // Disable if the child's gameObject.name is not found in possibleSettings
                var child = _sidebarContent.transform.GetChild(i);

#nullable enable
                (string setting, string? overrideText)? settingInfo = null;
#nullable disable

                foreach (var possibleSetting in possibleSettings)
                {
                    if (possibleSetting.setting == child.gameObject.name)
                    {
                        settingInfo = possibleSetting;
                        break;
                    }
                }

                if (settingInfo is null)
                {
                    child.gameObject.SetActive(false);
                }
                else
                {
                    child.gameObject.SetActive(true);
                    if (settingInfo.Value.overrideText is not null)
                    {
                        child.gameObject.transform.Find("Option Name").GetComponent<TextMeshProUGUI>().text = settingInfo.Value.overrideText;
                    }
                }
            }
        }

        public void HideContents()
        {
            ExitNavigation();
            _contents.SetActive(false);
        }

        public void SetNameEditMode(bool editing)
        {
            _nameContainer.SetActive(!editing);
            _editNameContainer.SetActive(editing);

            if (editing)
            {
                _nameInput.text = _profile.Name;
                _nameInput.Select();
            }
            else
            {
                // Set the name. Make sure to record the name change in the scores.
                _profile.Name = _nameInput.text;
                ScoreContainer.RecordPlayerInfo(_profile.Id, _profile.Name);

                // Update the UI
                _profileName.text = _profile.Name;
                _profileView.UpdateDisplay(_profile);
            }
        }

        public void EditProfile()
        {
            // Only allow profile editing if it's taken
            if (!PlayerContainer.IsProfileTaken(_profile))
            {
                return;
            }

            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.ProfileInfo, false);

            menu.GetComponent<ProfileInfoMenu>().CurrentProfile = _profile;
            menu.gameObject.SetActive(true);
        }

        public void AddDevice()
        {
            _profileView.PromptAddDevice().Forget();
        }

        public void RemoveDevice()
        {
            _profileView.PromptRemoveDevice().Forget();
        }

        public void ChangeGameMode()
        {
            _profile.GameMode = _gameModesByIndex[_gameModeDropdown.value];

            // Set the player's instrument to the foremost of their new game mode's possible instruments. This prevents scenarios like
            // a brand new Keys profile defaulting to 5L Lead Guitar instead of Pro Keys
            _profile.CurrentInstrument = _profile.GameMode.PossibleInstruments()[0];

            _profileView.UpdateDisplay(_profile);
            FiltersMenu.ResetIntensityFiltersForProfile(_profile);
            // Update sidebar when game mode changes so the correct settings are displayed
            UpdateSidebar(_profile, _profileView);
        }

        public void ChangeNoteSpeed()
        {
            if (float.TryParse(_noteSpeedField.text, out var speed))
            {
                _profile.NoteSpeed = Mathf.Clamp(speed, 0f, 100f);
            }

            // Always format it after
            _noteSpeedField.text = _profile.NoteSpeed.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
        }

        public void ChangeHighwayLength()
        {
            if (float.TryParse(_highwayLengthField.text, out var speed))
            {
                _profile.HighwayLength = Mathf.Clamp(speed, 0.001f, 10f);
            }

            // Always format it after
            _highwayLengthField.text = _profile.HighwayLength.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
        }

        public void ChangeInputCalibration()
        {
            if (long.TryParse(_inputCalibrationField.text, out long calibration))
            {
                _profile.InputCalibrationMilliseconds = calibration;
            }

            // Always format it after
            _inputCalibrationField.text = _profile.InputCalibrationMilliseconds.ToString();
        }

        public void ChangeLeftyFlip()
        {
            _profile.LeftyFlip = _leftyFlipToggle.isOn;
        }

        public void ChangeRangeDisabled()
        {
            _profile.RangeEnabled = _rangeDisabledToggle.isOn;
        }

        public void ChangeUseCymbalModels()
        {
            _profile.UseCymbalModels = _useCymbalModelsToggle.isOn;
        }

        public void ChangeEngine()
        {
            _profile.EnginePreset = _enginePresetsByIndex[_engineDropdown.value];
        }

        public void ChangeOpenLaneDisplayType()
        {
            _profile.OpenLaneDisplayType = _openLaneDisplayTypesByIndex[_openLaneDisplayTypeDropdown.value];
        }

        public void ChangeStarPowerActivationType()
        {
            _profile.StarPowerActivationType = _starPowerActivationTypesByIndex[_starPowerActivationTypeDropdown.value];
        }

        public void ChangeTheme()
        {
            var themeGuid = _themesByIndex[_themeDropdown.value];

            // Skip if there are no changes
            if (themeGuid == _profile.ThemePreset) return;

            _profile.ThemePreset = themeGuid;

            var themePreset = CustomContentManager.ThemePresets.GetPresetById(themeGuid);

            bool hasPresets = false;
            var presets = string.Empty;

            // Check camera presets
            if (CustomContentManager.CameraSettings
                .TryGetPresetById(themePreset.PreferredCameraPreset, out var cameraPreset))
            {
                hasPresets = true;
                presets += $"<color=yellow>Camera Preset: {cameraPreset.Name}</color>\n";
            }

            // Check color profiles
            if (CustomContentManager.ColorProfiles
                .TryGetPresetById(themePreset.PreferredColorProfile, out var colorProfile))
            {
                hasPresets = true;
                presets += $"<color=yellow>Color Profile: {colorProfile.Name}</color>\n";
            }

            // Skip if there are no preferred presets
            if (!hasPresets) return;

            // Ask user if they'd like to apply the preferred presets
            var dialog = DialogManager.Instance.ShowMessage("Apply Recommended Presets?",
                "This theme has recommended presets. These presets will make the theme look as intended. " +
                "Would you like to apply them?\n\n" + presets.Trim());
            dialog.ClearButtons();

            // Add buttons

            dialog.AddDialogButton("Menu.Common.DontApply", MenuData.Colors.CancelButton,
                () => DialogManager.Instance.ClearDialog());

            dialog.AddDialogButton("Menu.Common.Apply", MenuData.Colors.ConfirmButton, () =>
            {
                _profile.CameraPreset = cameraPreset?.Id ?? CameraPreset.Default.Id;
                _profile.ColorProfile = colorProfile?.Id ?? ColorProfile.Default.Id;

                UpdateSidebar(_profile, _profileView);

                DialogManager.Instance.SubmitAndClearDialog();
            });
        }

        public void ChangeColorProfile()
        {
            _profile.ColorProfile = _colorProfilesByIndex[_colorProfileDropdown.value];
        }

        public void ChangeCameraPreset()
        {
            _profile.CameraPreset = _cameraPresetsByIndex[_cameraPresetDropdown.value];
        }

        public void ChangeHighwayPreset()
        {
            _profile.HighwayPreset = _highwayPresetsByIndex[_highwayPresetDropdown.value];
        }

        public void ChangeRockMeterPreset()
        {
            _profile.RockMeterPreset = _rockmeterPresetsByIndex[_rockMeterPresetDropdown.value];
        }
    }
}
