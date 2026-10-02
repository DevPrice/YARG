using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YARG.Menu.Navigation;

namespace YARG.Input
{
    /// <summary>
    /// A software mouse cursor driven by any gamepad, for screens that only respond to the pointer.
    /// Clicking the left stick shows it; clicking it again or pressing B hides it. While it is shown,
    /// menu navigation is blocked so the same button presses don't also act on the menu.
    /// </summary>
    public sealed class GamepadCursor : MonoBehaviour
    {
        private const float CURSOR_SIZE_PER_SCREEN_HEIGHT = 0.045f;
        private const float CURSOR_SPEED_SCREEN_HEIGHTS_PER_SECOND = 1.1f;
        private const float SCROLL_SPEED = 2f;
        private const int CURSOR_TEXTURE_SIZE = 64;

        private static GamepadCursor _instance;

        private InputAction _toggleAction;
        private InputAction _hideAction;

        private GameObject _cursorRoot;
        private RectTransform _cursorTransform;
        private VirtualMouseInput _virtualMouse;

        private IDisposable _navigationBlocker;

        public static bool IsShown => _instance != null && _instance._cursorRoot.activeSelf;

        public static void SetAvailable(bool available)
        {
            if (available && _instance == null)
            {
                var gameObject = new GameObject(nameof(GamepadCursor));
                DontDestroyOnLoad(gameObject);
                _instance = gameObject.AddComponent<GamepadCursor>();
            }
            else if (!available && _instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private void Awake()
        {
            BuildCursor();

            _toggleAction = new InputAction("GamepadCursor_Toggle", InputActionType.Button, "<Gamepad>/leftStickPress");
            _toggleAction.performed += _ => SetShown(!IsShown);
            _toggleAction.Enable();

            // Hide on release: B is also the Back menu action, and its press must still find navigation
            // blocked, or hiding the cursor would also leave the current menu
            _hideAction = new InputAction("GamepadCursor_Hide", InputActionType.Button, "<Gamepad>/buttonEast");
            _hideAction.canceled += _ => SetShown(false);
            _hideAction.Enable();

            // Navigation is blocked while the cursor is up, which would also block gameplay's pause menu
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SetShown(false);

            _toggleAction.Dispose();
            _hideAction.Dispose();
            _virtualMouse.stickAction.action?.Dispose();
            _virtualMouse.leftButtonAction.action?.Dispose();
            _virtualMouse.rightButtonAction.action?.Dispose();
            _virtualMouse.scrollWheelAction.action?.Dispose();
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            SetShown(false);
        }

        private void SetShown(bool shown)
        {
            if (_cursorRoot == null || _cursorRoot.activeSelf == shown)
            {
                return;
            }

            if (shown)
            {
                if (Navigator.Instance == null)
                {
                    return;
                }

                _navigationBlocker = Navigator.Instance.PushInputBlocker();

                float height = Screen.height;
                float size = Mathf.Round(height * CURSOR_SIZE_PER_SCREEN_HEIGHT);
                _cursorTransform.sizeDelta = new Vector2(size, size);
                _cursorTransform.anchoredPosition = new Vector2(Screen.width / 2f, height / 2f);
                _virtualMouse.cursorSpeed = height * CURSOR_SPEED_SCREEN_HEIGHTS_PER_SECOND;
            }
            else
            {
                _navigationBlocker?.Dispose();
                _navigationBlocker = null;
            }

            // VirtualMouseInput adds its mouse device on enable and removes it on disable, so a hidden
            // cursor can't leave a hover state behind on whatever is under it
            _cursorRoot.SetActive(shown);

            if (shown)
            {
                // The setter looks up the canvas that bounds the cursor, which fails while inactive
                _virtualMouse.cursorGraphic = _virtualMouse.cursorGraphic;
            }
        }

        private void BuildCursor()
        {
            _cursorRoot = new GameObject("Cursor", typeof(RectTransform));
            _cursorRoot.SetActive(false);
            _cursorRoot.transform.SetParent(transform, false);

            // No CanvasScaler: VirtualMouseInput writes screen pixels to anchoredPosition, so the canvas
            // has to stay at a scale factor of 1
            var canvas = _cursorRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var imageObject = new GameObject("Image", typeof(RectTransform));
            imageObject.transform.SetParent(_cursorRoot.transform, false);

            _cursorTransform = (RectTransform) imageObject.transform;
            _cursorTransform.anchorMin = Vector2.zero;
            _cursorTransform.anchorMax = Vector2.zero;
            _cursorTransform.pivot = new Vector2(0.5f, 0.5f);

            var image = imageObject.AddComponent<Image>();
            image.sprite = CreateCursorSprite();
            image.raycastTarget = false;

            _virtualMouse = _cursorRoot.AddComponent<VirtualMouseInput>();
            _virtualMouse.cursorMode = VirtualMouseInput.CursorMode.SoftwareCursor;
            _virtualMouse.cursorGraphic = image;
            _virtualMouse.cursorTransform = _cursorTransform;
            _virtualMouse.scrollSpeed = SCROLL_SPEED;
            _virtualMouse.stickAction = new InputActionProperty(
                new InputAction("GamepadCursor_Move", InputActionType.Value, "<Gamepad>/leftStick", expectedControlType: "Vector2"));
            _virtualMouse.leftButtonAction = new InputActionProperty(
                new InputAction("GamepadCursor_LeftClick", InputActionType.Button, "<Gamepad>/buttonSouth"));
            _virtualMouse.rightButtonAction = new InputActionProperty(
                new InputAction("GamepadCursor_RightClick", InputActionType.Button, "<Gamepad>/buttonWest"));
            _virtualMouse.scrollWheelAction = new InputActionProperty(
                new InputAction("GamepadCursor_Scroll", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2"));
        }

        private static Sprite CreateCursorSprite()
        {
            const int size = CURSOR_TEXTURE_SIZE;
            const float outer = size / 2f;
            const float border = outer - size / 8f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var fill = new Color(1f, 1f, 1f, 0.9f);
            var outline = new Color(0f, 0f, 0f, 0.9f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(outer, outer));
                    pixels[y * size + x] = distance <= border ? fill
                        : distance <= outer ? outline
                        : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
