using UnityEngine;
using UnityEngine.UIElements;

namespace Utils
{
    /// <summary>
    /// ENGINE: keyboard shortcuts that stand aside while the player types. Hotkeys read keys through KeyDown / Key
    /// instead of UnityEngine.Input; both say no while a UI Toolkit text field has keyboard focus, so typing a name
    /// doesn't pause the game, move the camera or open screens. Checked once a frame (the UI documents are
    /// looked up again every couple of seconds).
    /// </summary>
    public static class KeyboardFocus
    {
        static int checkedFrame = -1;
        static bool typing;
        static UIDocument[] documents;
        static float documentsTime = float.NegativeInfinity;
        const float DocumentsRefreshSeconds = 2f; // presentation throttle: real time

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            checkedFrame = -1;
            typing = false;
            documents = null;
            documentsTime = float.NegativeInfinity;
        }

        /// <summary>A UI Toolkit text field has keyboard focus (the player is typing).</summary>
        public static bool Typing
        {
            get
            {
                if (checkedFrame != Time.frameCount)
                {
                    checkedFrame = Time.frameCount;
                    typing = FindFocusedText();
                }
                return typing;
            }
        }

        /// <summary>Input.GetKeyDown, except while typing.</summary>
        public static bool KeyDown(KeyCode key) => !Typing && Input.GetKeyDown(key);

        /// <summary>Input.GetKey, except while typing.</summary>
        public static bool Key(KeyCode key) => !Typing && Input.GetKey(key);

        static bool FindFocusedText()
        {
            if (documents == null || Time.unscaledTime - documentsTime > DocumentsRefreshSeconds)
            {
                documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                documentsTime = Time.unscaledTime;
            }
            foreach (var doc in documents)
            {
                if (doc == null || doc.rootVisualElement?.panel == null) continue;
                for (var e = doc.rootVisualElement.panel.focusController?.focusedElement as VisualElement; e != null; e = e.parent)
                    if (e is TextField) return true;
            }
            return false;
        }
    }
}
