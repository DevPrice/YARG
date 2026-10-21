using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using YARG.Core.Input;

namespace YARG.Menu.Navigation
{
    /// <summary>
    /// Lets a gamepad start and finish editing a <see cref="TMP_InputField"/>. A focused field blocks menu
    /// navigation, and nothing else lets a gamepad end the edit. Where the platform has a system on-screen
    /// keyboard (<see cref="TouchScreenKeyboard.isSupported"/>, such as the Xbox), TMP opens it when the field
    /// is activated, and that keyboard handles typing, accepting and cancelling.
    /// </summary>
    public static class GamepadTextEntry
    {
        /// <summary>
        /// Focuses <paramref name="field"/> for editing, opening the system keyboard where there is one.
        /// </summary>
        public static void Activate(TMP_InputField field)
        {
            if (field == null || !field.isActiveAndEnabled || !field.interactable)
            {
                return;
            }

            field.ActivateInputField();
        }

        /// <summary>
        /// Returns a copy of <paramref name="scheme"/> that also activates <paramref name="field"/> on Yellow,
        /// so a gamepad player can get back into a field after leaving it.
        /// </summary>
        public static NavigationScheme WithEditEntry(NavigationScheme scheme, TMP_InputField field)
        {
            var entries = new List<NavigationScheme.Entry>(scheme.Entries)
            {
                new(MenuAction.Yellow, "Menu.Common.EditText", () => Activate(field)),
            };

            return new NavigationScheme(entries, scheme.AllowsMusicPlayer, scheme.PopCallback)
            {
                SuppressHelpBar = scheme.SuppressHelpBar
            };
        }

        /// <summary>
        /// While <paramref name="field"/> is being edited: South or Start submits it, and East ends the edit
        /// keeping the text, as clicking elsewhere does. Gamepads are read directly rather than through menu
        /// actions, because the default keyboard menu bindings include keys that are typed into fields.
        /// </summary>
        internal static void HandleFocusedField(TMP_InputField field)
        {
            // The system keyboard reads the gamepad itself while it's open
            if (field.touchScreenKeyboard is { status: TouchScreenKeyboard.Status.Visible })
            {
                return;
            }

            foreach (var gamepad in Gamepad.all)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame)
                {
                    field.OnSubmit(null);
                    ReleaseSelection(field);
                    return;
                }

                if (gamepad.buttonEast.wasPressedThisFrame)
                {
                    field.DeactivateInputField();
                    ReleaseSelection(field);
                    return;
                }
            }
        }

        private static void ReleaseSelection(TMP_InputField field)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting &&
                eventSystem.currentSelectedGameObject == field.gameObject)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }
    }
}
