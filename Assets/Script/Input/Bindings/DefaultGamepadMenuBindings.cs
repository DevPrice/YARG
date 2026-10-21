using System;
using UnityEngine.InputSystem;
using YARG.Core.Input;
using YARG.Player;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_WSA
using UnityEngine.InputSystem.Switch;
#endif

namespace YARG.Input
{
    /// <summary>
    /// Lets gamepads that no connected player owns drive the menus, so a controller-only
    /// setup (such as a console) can get to the profile menu without a keyboard or mouse.
    /// </summary>
    public sealed class DefaultGamepadMenuBindings : IDisposable
    {
        // Mirrors BindingCollection.SetDefaultMenuBindings_Gamepad
        private static readonly (MenuAction action, string path)[] DefaultBindings =
        {
            (MenuAction.Green,  "<Gamepad>/buttonSouth"),
            (MenuAction.Red,    "<Gamepad>/buttonEast"),
            (MenuAction.Yellow, "<Gamepad>/buttonNorth"),
            (MenuAction.Blue,   "<Gamepad>/buttonWest"),
            (MenuAction.Orange, "<Gamepad>/leftShoulder"),
            (MenuAction.Start,  "<Gamepad>/start"),
            (MenuAction.Select, "<Gamepad>/select"),
            (MenuAction.Up,     "<Gamepad>/dpad/up"),
            (MenuAction.Down,   "<Gamepad>/dpad/down"),
            (MenuAction.Left,   "<Gamepad>/dpad/left"),
            (MenuAction.Right,  "<Gamepad>/dpad/right"),
            (MenuAction.Up,     "<Gamepad>/leftStick/up"),
            (MenuAction.Down,   "<Gamepad>/leftStick/down"),
            (MenuAction.Left,   "<Gamepad>/leftStick/left"),
            (MenuAction.Right,  "<Gamepad>/leftStick/right"),
            (MenuAction.Search,       "<Gamepad>/rightStickPress"),
            (MenuAction.SelectArtist, "<Gamepad>/rightShoulder"),
        };

        private readonly InputAction[] _inputActions = new InputAction[DefaultBindings.Length];

        // The action sent on press, so the release matches it even if the device was claimed by a
        // player in between
        private readonly MenuAction?[] _pressedActions = new MenuAction?[DefaultBindings.Length];

        public DefaultGamepadMenuBindings()
        {
            for (int i = 0; i < DefaultBindings.Length; i++)
            {
                int index = i;
                var (menuAction, path) = DefaultBindings[i];

                var action = new InputAction(
                    name: $"GamepadMenu_{menuAction}_{index}",
                    type: InputActionType.Button,
                    binding: path
                );

                action.performed += ctx => OnPressed(index, menuAction, ctx.control.device);
                action.canceled += _ => OnReleased(index);
                action.Enable();
                _inputActions[i] = action;
            }
        }

        private void OnPressed(int index, MenuAction menuAction, InputDevice device)
        {
            if (PlayerContainer.IsDeviceTaken(device))
            {
                return;
            }

            menuAction = ApplyLayoutSwaps(menuAction, device);
            _pressedActions[index] = menuAction;
            InputManager.OnMenuAction(menuAction, true);
        }

        private void OnReleased(int index)
        {
            if (_pressedActions[index] is not { } menuAction)
            {
                return;
            }

            _pressedActions[index] = null;
            InputManager.OnMenuAction(menuAction, false);
        }

        private static MenuAction ApplyLayoutSwaps(MenuAction menuAction, InputDevice device)
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_WSA
            if (device is SwitchProControllerHID)
            {
                return menuAction switch
                {
                    MenuAction.Green  => MenuAction.Red,
                    MenuAction.Red    => MenuAction.Green,
                    MenuAction.Yellow => MenuAction.Blue,
                    MenuAction.Blue   => MenuAction.Yellow,
                    _                 => menuAction
                };
            }
#endif

            return menuAction;
        }

        public void Dispose()
        {
            foreach (var action in _inputActions)
            {
                action.Disable();
                action.Dispose();
            }
        }
    }
}
