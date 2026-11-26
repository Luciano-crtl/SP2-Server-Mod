using System;
using UnityEngine.EventSystems;

namespace SP2ServerMod
{
    internal static class UiFocus
    {
        // True while the player is typing in a text box (the game's chat, a panel field): the mod's keys must not fire then.
        public static bool TypingInATextField()
        {
            try
            {
                EventSystem system = EventSystem.current;
                UnityEngine.GameObject selected = system != null ? system.currentSelectedGameObject : null;
                if (selected == null) return false;
                TMPro.TMP_InputField field = selected.GetComponent<TMPro.TMP_InputField>();
                if (field != null) return field.isFocused;
                UnityEngine.UI.InputField legacy = selected.GetComponent<UnityEngine.UI.InputField>();
                return legacy != null && legacy.isFocused;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
