using UnityEngine;
using UnityEngine.UI;
using YARG.Helpers.Extensions;

namespace YARG.Menu.Navigation
{
    [RequireComponent(typeof(NavigationGroup), typeof(ScrollRect))]
    public class ScrollViewNavigationUpdater : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _contentTransform;

        private NavigationGroup _navigationGroup;
        private ScrollRect _scrollRect;
        private RectTransform _viewportTransform;

        private void Awake()
        {
            _navigationGroup = GetComponent<NavigationGroup>();
            _scrollRect = GetComponent<ScrollRect>();
            _viewportTransform = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : GetComponent<RectTransform>();

            _navigationGroup.SelectionChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged(NavigatableBehaviour selected, SelectionOrigin selectionOrigin)
        {
            // Only scroll it automatically if it's a navigation selection type
            if (selectionOrigin != SelectionOrigin.Navigation || selected == null)
                return;

            ScrollIntoView(_scrollRect, _viewportTransform, _contentTransform, selected.transform as RectTransform);
        }

        /// <summary>
        /// Scrolls <paramref name="scrollRect"/> vertically by the least amount that brings
        /// <paramref name="target"/> fully into <paramref name="viewport"/>.
        /// </summary>
        public static void ScrollIntoView(ScrollRect scrollRect, RectTransform viewport,
            RectTransform content, RectTransform target)
        {
            Canvas.ForceUpdateCanvases();

            if (scrollRect.ScrollableHeight() <= 0f)
            {
                scrollRect.verticalNormalizedPosition = 1f;
                return;
            }

            if (target == null) return;

            var viewportBounds = new Bounds(viewport.rect.center, viewport.rect.size);
            var selectedBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport, target);

            var newPos = content.anchoredPosition.y;
            if (selectedBounds.max.y > viewportBounds.max.y)
            {
                newPos -= selectedBounds.max.y - viewportBounds.max.y;
            }
            else if (selectedBounds.min.y < viewportBounds.min.y)
            {
                newPos += viewportBounds.min.y - selectedBounds.min.y;
            }
            else
            {
                return;
            }

            newPos = Mathf.Clamp(newPos, 0f, scrollRect.ScrollableHeight());
            content.anchoredPosition = content.anchoredPosition.WithY(newPos);
        }

        private void OnDestroy()
        {
            _navigationGroup.SelectionChanged -= OnSelectionChanged;
        }
    }
}