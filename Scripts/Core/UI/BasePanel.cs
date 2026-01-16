using UnityEngine;
using UnityEngine.UIElements;

namespace Core.UI
{
    /// <summary>
    /// ENGINE - Base class for UI Toolkit panels.
    /// Provides common infrastructure: UIDocument access, root element, show/hide.
    ///
    /// Usage:
    /// 1. Inherit from BasePanel
    /// 2. Override CreateUI() to build your panel
    /// 3. Call Initialize() to set up the panel
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class BasePanel : MonoBehaviour
    {
        // UI Document
        protected UIDocument uiDocument;
        protected VisualElement rootElement;
        protected VisualElement panelContainer;

        // State
        protected bool isInitialized;
        protected bool isVisible;

        /// <summary>True if panel has been initialized.</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>True if panel is currently visible.</summary>
        public bool IsVisible => isVisible;

        /// <summary>
        /// Initialize the panel. Call from subclass Initialize() method.
        /// </summary>
        protected bool InitializeBase()
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning($"{GetType().Name}: Already initialized!", "core_ui");
                return false;
            }

            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError($"{GetType().Name}: UIDocument not found!", "core_ui");
                return false;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError($"{GetType().Name}: Root VisualElement is null!", "core_ui");
                return false;
            }

            // Create the panel UI
            CreateUI();

            if (panelContainer != null)
            {
                rootElement.Add(panelContainer);
            }

            isInitialized = true;
            return true;
        }

        /// <summary>
        /// Override to create your panel's UI elements.
        /// Set panelContainer to your main container element.
        /// </summary>
        protected abstract void CreateUI();

        /// <summary>
        /// Show the panel.
        /// </summary>
        public virtual void Show()
        {
            if (panelContainer != null)
            {
                panelContainer.style.display = DisplayStyle.Flex;
                isVisible = true;
                OnShow();
            }
        }

        /// <summary>
        /// Hide the panel.
        /// </summary>
        public virtual void Hide()
        {
            if (panelContainer != null)
            {
                panelContainer.style.display = DisplayStyle.None;
                isVisible = false;
                OnHide();
            }
        }

        /// <summary>
        /// Toggle panel visibility.
        /// </summary>
        public virtual void Toggle()
        {
            if (isVisible)
                Hide();
            else
                Show();
        }

        /// <summary>
        /// Called when panel is shown. Override for refresh logic.
        /// </summary>
        protected virtual void OnShow() { }

        /// <summary>
        /// Called when panel is hidden. Override for cleanup.
        /// </summary>
        protected virtual void OnHide() { }

        /// <summary>
        /// Create the main panel container with common styling.
        /// </summary>
        protected VisualElement CreatePanelContainer(string name, Color backgroundColor, float padding = 10f, float borderRadius = 6f)
        {
            panelContainer = UIHelper.CreatePanel(name, backgroundColor, padding, borderRadius);
            return panelContainer;
        }

        /// <summary>
        /// Position the panel absolutely.
        /// </summary>
        protected void PositionPanel(float? top = null, float? right = null, float? bottom = null, float? left = null)
        {
            if (panelContainer != null)
            {
                UIHelper.SetAbsolutePosition(panelContainer, top, right, bottom, left);
            }
        }

        /// <summary>
        /// Center the panel on screen.
        /// </summary>
        protected void CenterPanel()
        {
            if (panelContainer != null)
            {
                panelContainer.style.position = Position.Absolute;
                panelContainer.style.left = new Length(50, LengthUnit.Percent);
                panelContainer.style.top = new Length(50, LengthUnit.Percent);
                panelContainer.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            }
        }
    }
}
