using UnityEngine;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu.ProfileInfo
{
    public class ProfileInfoMenu : MonoBehaviour
    {
        public YargProfile CurrentProfile { get; set; }

        [SerializeField]
        private HeaderTabs _tabs;

        [Space]
        [SerializeField]
        private GameObject _overviewTab;
        [SerializeField]
        private GameObject _editBindsTab;

        // Long enough that holding a fret or button to test its bind doesn't leave the menu
        private const float BACK_HOLD_SECONDS = 1.5f;

        private EditBindsTab _editBinds;

        private void Awake()
        {
            _editBinds = _editBindsTab.GetComponent<EditBindsTab>();
        }

        private void OnEnable()
        {
            _tabs.TabChanged += OnTabChanged;
            _tabs.SelectFirstTab();

            var nextTab = _tabs.NavigateNextTab;
            var previousTab = _tabs.NavigatePreviousTab;

            // While binds are shown, the edited player's taps are presses made to test them, so only a
            // hold of Back counts from that player
            _ = Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back",
                    ctx => { if (AcceptsTap(ctx)) Back(); },
                    onHoldHandler: _ => { if (!_editBinds.IsCapturingControl) Back(); },
                    holdSeconds: BACK_HOLD_SECONDS),
                new NavigationScheme.Entry(MenuAction.Right, nextTab.LocalizationKey,
                    ctx => { if (AcceptsTap(ctx)) nextTab.Invoke(ctx); }),
                new NavigationScheme.Entry(MenuAction.Left, previousTab.LocalizationKey,
                    ctx => { if (AcceptsTap(ctx)) previousTab.Invoke(ctx); }),
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up",
                    ctx => { if (AcceptsTap(ctx) && IsShowingBinds) _editBinds.SelectPreviousGameMode(ctx.IsRepeat); },
                    hide: true),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down",
                    ctx => { if (AcceptsTap(ctx) && IsShowingBinds) _editBinds.SelectNextGameMode(ctx.IsRepeat); },
                    hide: true),
            }, true));
        }

        private bool IsShowingBinds => _editBinds.isActiveAndEnabled;

        private bool AcceptsTap(NavigationContext context)
        {
            if (!IsShowingBinds)
            {
                return true;
            }

            return !_editBinds.IsCapturingControl &&
                (context.Player == null || context.Player != _editBinds.CurrentPlayer);
        }

        private void Back()
        {
            MenuManager.Instance.PopMenu();
        }

        private void OnDisable()
        {
            _tabs.TabChanged -= OnTabChanged;

            Navigator.Instance.PopScheme();
        }

        public async void ShowQuickBind()
        {
            if (CurrentProfile is { GameMode: GameMode.FourLaneDrums or GameMode.ProKeys or
                GameMode.FiveLaneDrums or GameMode.EliteDrums})
            {
                var dialog = DialogManager.Instance.ShowFriendlyBindingDialog(CurrentProfile, CurrentProfile.GameMode);
                await dialog.WaitUntilClosed();
            }
            else
            {
                var dialog = DialogManager.Instance.ShowMessage("Unsupported Instrument Type",
                    "Quick binding is currently only supported for Drums and Keys.");
                await dialog.WaitUntilClosed();
            }
        }

        private void OnTabChanged(string tabId)
        {
            _overviewTab.SetActive(tabId == "overview");
            _editBindsTab.SetActive(tabId == "binds");
        }
    }
}