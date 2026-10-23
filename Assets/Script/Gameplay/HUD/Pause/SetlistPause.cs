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
            UpdateSkip();
            Setlist.Changed += UpdateSkip;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Setlist.Changed -= UpdateSkip;
        }

        // Skip with nothing next quits the show, and a setlist app can add or remove the songs after this one while
        // the game is paused
        private void UpdateSkip()
        {
            bool hasNext = Setlist.HasNext;
            if (_skipObject.activeSelf == hasNext)
            {
                return;
            }

            var skip = _skipObject.GetComponent<NavigatableBehaviour>();
            if (!hasNext)
            {
                skip.SetSelected(false, SelectionOrigin.Programmatically);
            }

            _skipObject.SetActive(hasNext);

            // The navigation group adds only active children when it wakes, which on the first pause comes after
            // this hides Skip
            if (hasNext && skip.NavigationGroup == null)
            {
                AddInSiblingOrder(skip);
            }
        }

        private void AddInSiblingOrder(NavigatableBehaviour skip)
        {
            var navigationGroup = GetComponentInChildren<NavigationGroup>();
            int sibling = _skipObject.transform.GetSiblingIndex();

            int index = 0;
            while (index < navigationGroup.Count &&
                navigationGroup.Navigatables[index].transform.GetSiblingIndex() < sibling)
            {
                index++;
            }

            navigationGroup.InsertNavigatable(index, skip);
        }

        public void Skip()
        {
            PauseMenuManager.Skip();
        }
    }
}
