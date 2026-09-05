using System.Collections.Generic;

namespace RouteRunner.Core
{
    // Unity's legacy KeyCode and Input System Key enums use different names as well as different numbers.
    public static class KeyNames
    {
        static readonly Dictionary<string, string> ToLegacy = new Dictionary<string, string> {
            { "Enter", "Return" }, { "Backquote", "BackQuote" }, { "LeftCtrl", "LeftControl" },
            { "RightCtrl", "RightControl" }, { "LeftMeta", "LeftWindows" }, { "RightMeta", "RightWindows" },
            { "ContextMenu", "Menu" }, { "NumLock", "Numlock" }, { "PrintScreen", "Print" }, { "ScrollLock", "ScrollLock" }
        };
        public static string Legacy(string key)
        {
            if (key.StartsWith("Digit")) return "Alpha" + key.Substring(5);
            if (key.StartsWith("Numpad")) return "Keypad" + key.Substring(6);
            return ToLegacy.TryGetValue(key, out string name) ? name : key;
        }
        public static string InputSystem(string key)
        {
            if (key.StartsWith("Alpha")) return "Digit" + key.Substring(5);
            if (key.StartsWith("Keypad")) return "Numpad" + key.Substring(6);
            foreach (var pair in ToLegacy) if (pair.Value == key) return pair.Key;
            return key;
        }
    }
}
