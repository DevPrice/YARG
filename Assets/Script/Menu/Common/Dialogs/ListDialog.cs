using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu.Dialogs
{
    public class ListDialog : Dialog
    {
        [Space]
        [SerializeField]
        private Transform _listContainer;
        [SerializeField]
        private ColoredButton _listButtonPrefab;

        private readonly List<NavigatableBehaviour> _listNavigatables = new();

        private ScrollRect _scrollRect;

        /// <summary>
        /// How many navigatables come before the list entries in navigation order.
        /// </summary>
        protected virtual int NavigatablesBeforeList => 0;

        protected virtual void Awake()
        {
            _scrollRect = _listContainer.GetComponentInParent<ScrollRect>(true);
            NavigationGroup.SelectionChanged += OnSelectionChanged;
        }

        protected virtual void OnDestroy()
        {
            NavigationGroup.SelectionChanged -= OnSelectionChanged;
        }

        protected override NavigationScheme GetNavigationScheme()
        {
            var scheme = base.GetNavigationScheme();

            var entries = new List<NavigationScheme.Entry>(scheme.Entries)
            {
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Close",
                    () => DialogManager.Instance.ClearDialog()),
            };

            return new NavigationScheme(entries, scheme.AllowsMusicPlayer);
        }

        public ColoredButton AddListButton(string text, UnityAction handler = null, bool closeOnClick = true)
        {
            var button = AddListEntry(_listButtonPrefab);

            button.Text.text = text;
            if (closeOnClick)
            {
                if (handler != null)
                {
                    button.OnClick.AddListener(() =>
                    {
                        handler();
                        DialogManager.Instance.ClearDialog();
                    });
                }
                else
                {
                    button.OnClick.AddListener(() => DialogManager.Instance.ClearDialog());
                }
            }
            else if (handler != null)
            {
                button.OnClick.AddListener(handler);
            }

            return button;
        }

        /// <summary>
        /// Instantiates <paramref name="prefab"/> into the list. An entry that has a
        /// <see cref="NavigatableBehaviour"/> or a <see cref="Button"/> joins the dialog's
        /// navigation after the previous entries, and the first one is selected.
        /// </summary>
        public T AddListEntry<T>(T prefab)
            where T : Object
        {
            var entry = Instantiate(prefab, _listContainer);

            var navigatable = GetOrAttachNavigatable(entry);
            if (navigatable != null)
            {
                int beforeList = NavigatablesBeforeList;
                int index = beforeList + CountLive(_listNavigatables);
                InsertContentNavigatable(index, navigatable);
                _listNavigatables.Add(navigatable);

                if (NavigationGroup.SelectedBehaviour == null)
                {
                    NavigationGroup.SelectAt(Mathf.Min(beforeList, NavigationGroup.Count - 1));
                }
            }

            return entry;
        }

        public void ClearList()
        {
            _listContainer.DestroyChildren();
            _listNavigatables.Clear();
        }

        public override void ClearDialog()
        {
            base.ClearDialog();

            ClearList();
        }

        /// <summary>
        /// Returns the entry's own navigatable, or attaches one that clicks its button.
        /// Null if the entry has neither.
        /// </summary>
        protected static NavigatableBehaviour GetOrAttachNavigatable(Object entry)
        {
            var gameObject = entry switch
            {
                GameObject go  => go,
                Component comp => comp.gameObject,
                _              => null
            };

            if (gameObject == null)
            {
                return null;
            }

            var navigatable = gameObject.GetComponentInChildren<NavigatableBehaviour>(true);
            if (navigatable != null)
            {
                return navigatable;
            }

            var button = gameObject.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                return null;
            }

            return RuntimeNavigatable.Attach(gameObject, () =>
            {
                if (button.interactable)
                {
                    button.onClick.Invoke();
                }
            });
        }

        /// <summary>
        /// Inserts content (anything above the dialog buttons) into navigation at
        /// <paramref name="index"/>, clamped because <see cref="Dialog.ClearButtons"/>
        /// also clears content navigatables that are still alive.
        /// </summary>
        protected void InsertContentNavigatable(int index, NavigatableBehaviour navigatable)
        {
            NavigationGroup.InsertNavigatable(Mathf.Clamp(index, 0, NavigationGroup.Count), navigatable);
        }

        /// <summary>
        /// Drops destroyed navigatables, which have already left the navigation group.
        /// </summary>
        protected static int CountLive(List<NavigatableBehaviour> navigatables)
        {
            navigatables.RemoveAll(n => n == null);
            return navigatables.Count;
        }

        private void OnSelectionChanged(NavigatableBehaviour selected, SelectionOrigin selectionOrigin)
        {
            if (selectionOrigin != SelectionOrigin.Navigation || selected == null || _scrollRect == null)
            {
                return;
            }

            if (!selected.transform.IsChildOf(_listContainer))
            {
                return;
            }

            var viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : (RectTransform) _scrollRect.transform;
            var content = _scrollRect.content != null
                ? _scrollRect.content
                : (RectTransform) _listContainer;

            ScrollViewNavigationUpdater.ScrollIntoView(_scrollRect, viewport, content,
                selected.transform as RectTransform);
        }
    }
}
