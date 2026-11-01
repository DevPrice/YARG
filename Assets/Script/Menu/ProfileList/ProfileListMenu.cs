using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Input;
using YARG.Localization;
using YARG.Menu.HighwayConfiguration;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings.Customization;
using static YARG.Core.Game.ColorProfile;
using static YARG.Menu.HighwayConfiguration.DrumsHighwayConfigurationMenu;

namespace YARG.Menu.ProfileList
{
    public class ProfileListMenu : MonoBehaviour
    {
        [SerializeField]
        private NavigationGroup _navigationGroup;

        [Space]
        [SerializeField]
        private ProfileSidebar _profileSidebar;
        [SerializeField]
        private Transform _profileList;

        [Space]
        [SerializeField]
        private GameObject _profileViewPrefab;
        [SerializeField]
        private GameObject _profileListHeaderPrefab;

        private readonly int _maxConnected = HighwayCameraRendering.MAX_MATRICES;

        public bool CanConnectProfile => PlayerContainer.Players.Count < _maxConnected;

        private ScrollRect _listScrollRect;

        // The list's help bar actions depend on the selected row, so its scheme is rebuilt when that changes
        private bool _listSchemePushed;
        private bool _listSchemeDirty;
        private bool _highwayConfigurationOpen;

        private void Awake()
        {
            _listScrollRect = _navigationGroup.GetComponent<ScrollRect>();
        }

        private void OnEnable()
        {
            // Keep the profile that was selected before opening a sub-menu such as Edit Profile
            RefreshList(GetSelectedProfile());

            _navigationGroup.SelectionChanged += OnListSelectionChanged;

            PushListScheme().Forget();

            PlayerContainer.PlayerAdded += OnPlayerAdded;
        }

        private async UniTaskVoid PushListScheme()
        {
            // Like Navigator.PushScheme, let an open dialog close first so this lands above its scheme.
            // Without a dialog this completes synchronously.
            await DialogManager.Instance.WaitUntilCurrentClosed();
            if (!isActiveAndEnabled || _listSchemePushed)
            {
                return;
            }

            Navigator.Instance.PushSchemeImmediate(BuildListScheme());
            _listSchemePushed = true;
            _listSchemeDirty = false;
        }

        private void OnDisable()
        {
            PlayerContainer.EnsureValidInstruments();
            PlayerContainer.SaveProfiles();

            // Update player icons if a profile has changed its GameMode.
            // Persistent singletons may already be destroyed when Unity exits play mode.
            StatsManager.Instance?.UpdateActivePlayers();

            // The sidebar's scheme sits on top of the list's
            _profileSidebar.ExitNavigation();
            if (_listSchemePushed)
            {
                _listSchemePushed = false;
                Navigator.Instance?.PopScheme();
            }

            _navigationGroup.SelectionChanged -= OnListSelectionChanged;
            PlayerContainer.PlayerAdded -= OnPlayerAdded;
        }

        private void Update()
        {
            if (_listSchemeDirty && IsListSchemeCurrent())
            {
                _listSchemeDirty = false;

                Navigator.Instance.PopScheme();
                Navigator.Instance.PushSchemeImmediate(BuildListScheme());
            }
        }

        /// <summary>
        /// Whether the list's scheme is the top of the navigator's stack, so it can be swapped out.
        /// </summary>
        private bool IsListSchemeCurrent()
        {
            return _listSchemePushed && !_profileSidebar.IsNavigating && !_profileSidebar.IsDropdownListOpen &&
                !_highwayConfigurationOpen && !DialogManager.Instance.IsDialogShowing;
        }

        private NavigationScheme BuildListScheme()
        {
            var entries = new List<NavigationScheme.Entry>
            {
                new(MenuAction.Red, "Menu.Common.Back", () => MenuManager.Instance.PopMenu(), hide: true),
                new(MenuAction.Up, "Menu.Common.Up", ctx => _navigationGroup.SelectPrevious(ctx.IsRepeat)),
                new(MenuAction.Down, "Menu.Common.Down", ctx => _navigationGroup.SelectNext(ctx.IsRepeat)),
            };

            var view = _navigationGroup.SelectedBehaviour as ProfileView;
            if (view != null)
            {
                AddSelectedProfileEntries(entries, view);
            }

            entries.Add(new(MenuAction.Blue, "Menu.ProfileList.AddProfile", () => AddProfileAndSelect(false)));
            entries.Add(new(MenuAction.Orange, "Menu.ProfileList.AddBot", () => AddProfileAndSelect(true)));

            return new NavigationScheme(entries, true);
        }

        private void AddSelectedProfileEntries(List<NavigationScheme.Entry> entries, ProfileView view)
        {
            // The view is destroyed whenever the list is rebuilt, which can happen before this scheme is replaced
            void OnView(Action<ProfileView> action)
            {
                if (view != null)
                {
                    action(view);
                }
            }

            if (view.UnloadedRecord is not null)
            {
                entries.Add(new(MenuAction.Yellow, "Menu.Common.Delete", () => OnView(v => v.RemoveProfile())));
                return;
            }

            if (!PlayerContainer.IsProfileTaken(view.Profile))
            {
                entries.Add(new(MenuAction.Green, "Menu.ProfileList.Connect",
                    () => OnView(v => v.ConnectButtonAction())));
                entries.Add(new(MenuAction.Yellow, "Menu.Common.Delete", () => OnView(v => v.RemoveProfile())));
                return;
            }

            entries.Add(new(MenuAction.Green, "Menu.ProfileList.Settings", _profileSidebar.EnterNavigation));
            entries.Add(new(MenuAction.Yellow, "Menu.ProfileList.Disconnect", () => OnView(v =>
            {
                var profile = v.Profile;
                v.Disconnect();
                SetSelectedProfile(profile);
            })));

            if (PlayerContainer.Players.Count > 1)
            {
                entries.Add(new(MenuAction.Left, "Menu.ProfileList.MoveProfile",
                    () => OnView(v => MoveProfileUp(v.Profile))));
                entries.Add(new(MenuAction.Right, "Menu.ProfileList.MoveProfile",
                    () => OnView(v => MoveProfileDown(v.Profile))));
            }
        }

        private void OnListSelectionChanged(NavigatableBehaviour selected, SelectionOrigin selectionOrigin)
        {
            _listSchemeDirty = true;

            if (selectionOrigin == SelectionOrigin.Navigation && selected != null && _listScrollRect != null)
            {
                _listScrollRect.ScrollIntoView((RectTransform) selected.transform);
            }
        }

        public void RefreshList(YargProfile selectedProfile = null)
        {
            _listSchemeDirty = true;

            // Deselect
            _profileSidebar.HideContents();

            // Remove old ones
            _profileList.transform.DestroyChildren();
            _navigationGroup.ClearNavigatables();

            var activeProfiles = PlayerContainer.Players.Select(e => e.Profile).ToArray();
            var otherProfiles = PlayerContainer.Profiles.Except(activeProfiles).OrderBy(e => e.Name).ToArray();

            AddListGroup(Localize.Key("Menu.ProfileList.ActiveProfiles"), activeProfiles);
            AddListGroup(Localize.Key("Menu.ProfileList.Players"), otherProfiles.Where(e => !e.IsBot));
            AddListGroup(Localize.Key("Menu.ProfileList.Bots"), otherProfiles.Where(e => e.IsBot));
            AddUnloadedGroup(Localize.Key("Menu.ProfileList.CouldNotLoad"));

            if (selectedProfile == null)
            {
                return;
            }

            SetSelectedProfile(selectedProfile);
        }

        private void AddListGroup(string header, IEnumerable<YargProfile> profiles)
        {
            if (!profiles.Any())
            {
                return;
            }

            var headerGo = Instantiate(_profileListHeaderPrefab, _profileList);
            headerGo.GetComponentInChildren<TextMeshProUGUI>().text = header;
            _navigationGroup.AddNavigatable(headerGo);

            // Spawn in a profile view for each player
            foreach (var profile in profiles)
            {
                var go = Instantiate(_profileViewPrefab, _profileList);
                go.GetComponent<ProfileView>().Init(this, profile, _profileSidebar);
                _navigationGroup.AddNavigatable(go);
            }
        }

        private void AddUnloadedGroup(string header)
        {
            if (PlayerContainer.UnloadedProfiles.Count == 0)
            {
                return;
            }

            var headerGo = Instantiate(_profileListHeaderPrefab, _profileList);
            headerGo.GetComponentInChildren<TextMeshProUGUI>().text = header;
            _navigationGroup.AddNavigatable(headerGo);

            foreach (var record in PlayerContainer.UnloadedProfiles)
            {
                var go = Instantiate(_profileViewPrefab, _profileList);
                go.GetComponent<ProfileView>().InitUnloaded(this, record, _profileSidebar);
                _navigationGroup.AddNavigatable(go);
            }
        }

        // TODO: Since we're using this outside of ProfileListMenu, we should probably find a better home for it
        public static string GetUniqueProfileName(string profileName)
        {
            var existingNames = PlayerContainer.Profiles.Select(p => p.Name);

            if (!existingNames.Contains(profileName))
            {
                return profileName;
            }

            int count = 1;
            string newName;
            do
            {
                newName = $"{profileName} {count}";
                count++;
            } while (existingNames.Contains(newName));

            return newName;
        }

        public void AddProfile()
        {
            CreateProfile(false);
            RefreshList();
        }

        public void AddBotProfile()
        {
            CreateProfile(true);
            RefreshList();
        }

        // Selects the new row too, so a controller player doesn't have to find it in the list
        private void AddProfileAndSelect(bool isBot)
        {
            RefreshList(CreateProfile(isBot));

            if (_navigationGroup.SelectedBehaviour != null && _listScrollRect != null)
            {
                _listScrollRect.ScrollIntoView((RectTransform) _navigationGroup.SelectedBehaviour.transform);
            }
        }

        private static YargProfile CreateProfile(bool isBot)
        {
            var profile = new YargProfile
            {
                Name = GetUniqueProfileName(isBot ? "Bot" : "New Profile"),
                NoteSpeed = 5,
                HighwayLength = 1,
                GameMode = GameMode.FiveFretGuitar,
                IsBot = isBot
            };

            PlayerContainer.AddProfile(profile);
            return profile;
        }

        public void MoveProfileUp(YargProfile profile)
        {
            PlayerContainer.MoveUp(PlayerContainer.GetPlayerFromProfile(profile));
            RefreshList(profile);
        }

        public void MoveProfileDown(YargProfile profile)
        {
            PlayerContainer.MoveDown(PlayerContainer.GetPlayerFromProfile(profile));
            RefreshList(profile);
        }

        #nullable enable
        private YargProfile? GetSelectedProfile()
        #nullable disable
        {
            var profileView = _profileList.GetComponentsInChildren<ProfileView>()
                .FirstOrDefault(e => e.Selected);
            if (profileView != null)
            {
                return profileView.Profile;
            }

            return null;
        }

        public void SetSelectedProfile(YargProfile profile)
        {
            // Have to use LastOrDefault() here as this GetComponentsInChildren() call may include recently Destroyed objects.
            var profileView = _profileList.GetComponentsInChildren<ProfileView>()
                .LastOrDefault(e => e.Profile == profile);
            if (profileView != null)
            {
                profileView.SetSelected(true, SelectionOrigin.Programmatically);
            }
        }

        public void OnPlayerAdded(YargPlayer player)
        {
            RefreshList(GetSelectedProfile());
        }

        private void OpenDrumsHighwayConfigurationMenu(
            Dictionary<DrumsHighwayItem, HighwayOrderingItemSpec> specs,
            IFretColorProvider colorProvider,
            List<DrumsHighwayItem> defaultList,
            string header,
            SetOrdering setOrderingInProfile,
            Instrument instrument,
            YargProfile profile
        ) {
            var menu = DrumsHighwayConfigurationMenu.Instance;
            if (menu == null)
                return;


            menu.Initialize(
                specs,
                colorProvider,
                defaultList,
                Localize.Key("Menu.HighwayOrdering", header),
                setOrderingInProfile,
                instrument,
                profile
            );

            menu.gameObject.SetActive(true);

            if (!_highwayConfigurationOpen)
            {
                PushHighwayConfigurationScheme(menu).Forget();
            }
        }

        // The ordering editor is pointer-driven, but Back must still close it
        private async UniTaskVoid PushHighwayConfigurationScheme(DrumsHighwayConfigurationMenu menu)
        {
            _highwayConfigurationOpen = true;

            Navigator.Instance.PushSchemeImmediate(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", CloseDrumsHighwayConfigurationMenu),
            }, null));

            bool cancelled = await UniTask.WaitUntil(() => menu == null || !menu.gameObject.activeSelf,
                cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();

            _highwayConfigurationOpen = false;

            if (!cancelled && Navigator.Instance != null)
            {
                Navigator.Instance.PopScheme();
            }
        }
        public void CloseDrumsHighwayConfigurationMenu()
        {
            var menu = DrumsHighwayConfigurationMenu.Instance;
            if (menu == null)
                return;

            menu.gameObject.SetActive(false);
        }

        public void OpenFourLaneDrumsHighwayConfigurationMenu()
        {
            var profile = GetSelectedProfile();
            var colorProvider = CustomContentManager.ColorProfiles.GetPresetById(profile.ColorProfile).FourLaneDrums;
            OpenDrumsHighwayConfigurationMenu(
                DrumsHighwaySpecs.FOUR_LANE_SPECS,
                colorProvider,
                profile.FourLaneDrumsHighwayOrdering.ToList(),
                "4LaneHeader",
                (newOrdering) => { profile.FourLaneDrumsHighwayOrdering = newOrdering.ToArray(); },
                Instrument.FourLaneDrums,
                profile
            );
        }


        public void OpenProDrumsHighwayConfigurationMenu()
        {
            var profile = GetSelectedProfile();
            var colorProvider = CustomContentManager.ColorProfiles.GetPresetById(profile.ColorProfile).FourLaneDrums;
            OpenDrumsHighwayConfigurationMenu(
                DrumsHighwaySpecs.PRO_DRUMS_SPECS,
                colorProvider,
                profile.ProDrumsHighwayOrdering.ToList(),
                "ProHeader",
                (newOrdering) => { profile.ProDrumsHighwayOrdering = newOrdering.ToArray(); },
                Instrument.ProDrums,
                profile
            );
        }

        public void OpenFiveLaneDrumsHighwayConfigurationMenu()
        {
            var profile = GetSelectedProfile();
            var colorProvider = CustomContentManager.ColorProfiles.GetPresetById(profile.ColorProfile).FiveLaneDrums;
            OpenDrumsHighwayConfigurationMenu(
                DrumsHighwaySpecs.FIVE_LANE_SPECS,
                colorProvider,
                profile.FiveLaneDrumsHighwayOrdering.ToList(),
                "5LaneHeader",
                (newOrdering) => { profile.FiveLaneDrumsHighwayOrdering = newOrdering.ToArray(); },
                Instrument.FiveLaneDrums,
                profile
            );
        }
    }
}
