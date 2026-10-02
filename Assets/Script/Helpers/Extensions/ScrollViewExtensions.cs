using System;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Helpers.Extensions
{
    /// <summary>
    /// Extensions for <see cref="ScrollRect"/> to handle scrolling in units (typically pixels) rather than normalized values.
    /// </summary>
    public static class ScrollViewExtensions
    {
        public static float ScrollableWidth(this ScrollRect scrollRect)
        {
            return Math.Max(0f, scrollRect.content.rect.width - scrollRect.viewport.rect.width);
        }

        public static float ScrollableHeight(this ScrollRect scrollRect)
        {
            return Math.Max(0f, scrollRect.content.rect.height - scrollRect.viewport.rect.height);
        }

        public static float HorizonalPositionInUnits(this ScrollRect scrollRect)
        {
            if (scrollRect.ScrollableWidth() == 0)
            {
                return 0f; // No scrolling needed
            }

            return scrollRect.horizontalNormalizedPosition * scrollRect.ScrollableWidth();
        }

        public static float VerticalPositionInUnits(this ScrollRect scrollRect)
        {
            if (scrollRect.ScrollableHeight() == 0)
            {
                return 0f; // No scrolling needed
            }
            return scrollRect.verticalNormalizedPosition * scrollRect.ScrollableHeight();
        }

        public static void MoveHorizontalInUnits(this ScrollRect scrollRect, float delta)
        {
            if (scrollRect.ScrollableWidth() == 0)
            {
                return; // No scrolling needed
            }

            var position = scrollRect.HorizonalPositionInUnits() + delta;
            scrollRect.horizontalNormalizedPosition = Mathf.Clamp(position / scrollRect.ScrollableWidth(), 0, 1);
        }

        public static void MoveVerticalInUnits(this ScrollRect scrollRect, float delta)
        {
            if (scrollRect.ScrollableHeight() == 0)
            {
                return; // No scrolling needed
            }

            var position = scrollRect.VerticalPositionInUnits() + delta;
            scrollRect.verticalNormalizedPosition = Mathf.Clamp(position / scrollRect.ScrollableHeight(), 0, 1);
        }

        /// <summary>
        /// Scrolls vertically by the smallest amount that brings <paramref name="target"/>, a descendant of the
        /// content, fully into the viewport. Assumes the content is anchored to the top.
        /// </summary>
        public static void ScrollIntoView(this ScrollRect scrollRect, RectTransform target)
        {
            if (scrollRect.viewport == null || scrollRect.ScrollableHeight() <= 0f)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();

            var viewport = scrollRect.viewport.rect;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, target);

            float overflow;
            if (bounds.max.y > viewport.yMax)
            {
                overflow = bounds.max.y - viewport.yMax;
            }
            else if (bounds.min.y < viewport.yMin)
            {
                overflow = bounds.min.y - viewport.yMin;
            }
            else
            {
                return;
            }

            var content = scrollRect.content;
            float y = Mathf.Clamp(content.anchoredPosition.y - overflow, 0f, scrollRect.ScrollableHeight());
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }
    }
}
