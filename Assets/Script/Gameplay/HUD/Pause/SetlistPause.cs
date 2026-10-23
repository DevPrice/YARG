using UnityEngine;
using YARG.Menu.Navigation;
using YARG.Playlists;

namespace YARG.Gameplay.HUD
{
    public class SetlistPause : GenericPause
    {
        [SerializeField]
        private GameObject _skipObject;

        protected override void OnEnable()
        {
            base.OnEnable();

            // Disable skip option if this is the last song
            if (!Setlist.HasNext)
            {
                HideSkip();
            }

            Setlist.Changed += OnSetlistChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Setlist.Changed -= OnSetlistChanged;
        }

        // A setlist app can clear the songs after this one while the game is paused, and Skip with nothing next
        // quits the show
        private void OnSetlistChanged()
        {
            if (_skipObject.activeSelf && !Setlist.HasNext)
            {
                HideSkip();
            }
        }

        private void HideSkip()
        {
            _skipObject.SetActive(false);
            var navigationGroup = GetComponentInChildren<NavigationGroup>();
            navigationGroup.RemoveNavigatable(_skipObject.GetComponent<NavigatableBehaviour>());
        }

        public void Skip()
        {
            PauseMenuManager.Skip();
        }
    }
}
