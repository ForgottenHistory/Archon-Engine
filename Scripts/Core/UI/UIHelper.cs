using UnityEngine;
using UnityEngine.UIElements;

namespace Core.UI
{
    /// <summary>
    /// ENGINE - Static helper methods for UI Toolkit styling.
    /// Reduces boilerplate when creating UI elements programmatically.
    /// </summary>
    public static class UIHelper
    {
        // ====================================================================
        // PADDING
        // ====================================================================

        /// <summary>Set uniform padding on all sides.</summary>
        public static void SetPadding(VisualElement element, float all)
        {
            element.style.paddingTop = all;
            element.style.paddingBottom = all;
            element.style.paddingLeft = all;
            element.style.paddingRight = all;
        }

        /// <summary>Set padding with vertical and horizontal values.</summary>
        public static void SetPadding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
        }

        /// <summary>Set padding with individual values.</summary>
        public static void SetPadding(VisualElement element, float top, float right, float bottom, float left)
        {
            element.style.paddingTop = top;
            element.style.paddingRight = right;
            element.style.paddingBottom = bottom;
            element.style.paddingLeft = left;
        }

        // ====================================================================
        // MARGIN
        // ====================================================================

        /// <summary>Set uniform margin on all sides.</summary>
        public static void SetMargin(VisualElement element, float all)
        {
            element.style.marginTop = all;
            element.style.marginBottom = all;
            element.style.marginLeft = all;
            element.style.marginRight = all;
        }

        /// <summary>Set margin with vertical and horizontal values.</summary>
        public static void SetMargin(VisualElement element, float vertical, float horizontal)
        {
            element.style.marginTop = vertical;
            element.style.marginBottom = vertical;
            element.style.marginLeft = horizontal;
            element.style.marginRight = horizontal;
        }

        /// <summary>Set margin with individual values.</summary>
        public static void SetMargin(VisualElement element, float top, float right, float bottom, float left)
        {
            element.style.marginTop = top;
            element.style.marginRight = right;
            element.style.marginBottom = bottom;
            element.style.marginLeft = left;
        }

        // ====================================================================
        // BORDER RADIUS
        // ====================================================================

        /// <summary>Set uniform border radius on all corners.</summary>
        public static void SetBorderRadius(VisualElement element, float all)
        {
            element.style.borderTopLeftRadius = all;
            element.style.borderTopRightRadius = all;
            element.style.borderBottomLeftRadius = all;
            element.style.borderBottomRightRadius = all;
        }

        /// <summary>Set border radius with individual values.</summary>
        public static void SetBorderRadius(VisualElement element, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            element.style.borderTopLeftRadius = topLeft;
            element.style.borderTopRightRadius = topRight;
            element.style.borderBottomRightRadius = bottomRight;
            element.style.borderBottomLeftRadius = bottomLeft;
        }

        // ====================================================================
        // BORDER WIDTH
        // ====================================================================

        /// <summary>Set uniform border width on all sides.</summary>
        public static void SetBorderWidth(VisualElement element, float all)
        {
            element.style.borderTopWidth = all;
            element.style.borderBottomWidth = all;
            element.style.borderLeftWidth = all;
            element.style.borderRightWidth = all;
        }

        /// <summary>Remove all borders.</summary>
        public static void RemoveBorders(VisualElement element)
        {
            SetBorderWidth(element, 0);
        }

        // ====================================================================
        // BORDER COLOR
        // ====================================================================

        /// <summary>Set uniform border color on all sides.</summary>
        public static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        // ====================================================================
        // POSITIONING
        // ====================================================================

        /// <summary>Position element absolutely at specified coordinates.</summary>
        public static void SetAbsolutePosition(VisualElement element, float? top = null, float? right = null, float? bottom = null, float? left = null)
        {
            element.style.position = Position.Absolute;
            if (top.HasValue) element.style.top = top.Value;
            if (right.HasValue) element.style.right = right.Value;
            if (bottom.HasValue) element.style.bottom = bottom.Value;
            if (left.HasValue) element.style.left = left.Value;
        }

        // ====================================================================
        // FLEXBOX
        // ====================================================================

        /// <summary>Set flex direction to row with optional alignment.</summary>
        public static void SetFlexRow(VisualElement element, Justify justify = Justify.FlexStart, Align align = Align.Stretch)
        {
            element.style.flexDirection = FlexDirection.Row;
            element.style.justifyContent = justify;
            element.style.alignItems = align;
        }

        /// <summary>Set flex direction to column with optional alignment.</summary>
        public static void SetFlexColumn(VisualElement element, Justify justify = Justify.FlexStart, Align align = Align.Stretch)
        {
            element.style.flexDirection = FlexDirection.Column;
            element.style.justifyContent = justify;
            element.style.alignItems = align;
        }

        // ====================================================================
        // SIZE
        // ====================================================================

        /// <summary>Set fixed width and height.</summary>
        public static void SetSize(VisualElement element, float width, float height)
        {
            element.style.width = width;
            element.style.height = height;
        }

        /// <summary>Set minimum width.</summary>
        public static void SetMinWidth(VisualElement element, float width)
        {
            element.style.minWidth = width;
        }

        /// <summary>Set maximum height.</summary>
        public static void SetMaxHeight(VisualElement element, float height)
        {
            element.style.maxHeight = height;
        }

        // ====================================================================
        // TEXT
        // ====================================================================

        /// <summary>Style a label with common text properties.</summary>
        public static void StyleLabel(Label label, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
        }

        /// <summary>Set text alignment.</summary>
        public static void SetTextAlign(VisualElement element, TextAnchor anchor)
        {
            element.style.unityTextAlign = anchor;
        }

        // ====================================================================
        // VISIBILITY
        // ====================================================================

        /// <summary>Show element.</summary>
        public static void Show(VisualElement element)
        {
            if (element != null)
                element.style.display = DisplayStyle.Flex;
        }

        /// <summary>Hide element.</summary>
        public static void Hide(VisualElement element)
        {
            if (element != null)
                element.style.display = DisplayStyle.None;
        }

        /// <summary>Set visibility based on condition.</summary>
        public static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null)
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ====================================================================
        // COMMON ELEMENT CREATION
        // ====================================================================

        /// <summary>Create a styled panel container.</summary>
        public static VisualElement CreatePanel(string name, Color backgroundColor, float padding = 10f, float borderRadius = 6f)
        {
            var panel = new VisualElement();
            panel.name = name;
            panel.style.backgroundColor = backgroundColor;
            SetPadding(panel, padding);
            SetBorderRadius(panel, borderRadius);
            return panel;
        }

        /// <summary>Create a styled label.</summary>
        public static Label CreateLabel(string text, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            StyleLabel(label, fontSize, color, fontStyle);
            return label;
        }

        /// <summary>Create a styled button.</summary>
        public static Button CreateButton(string text, System.Action onClick, int fontSize = 14, Color? backgroundColor = null)
        {
            var button = new Button(onClick);
            button.text = text;
            button.style.fontSize = fontSize;
            if (backgroundColor.HasValue)
                button.style.backgroundColor = backgroundColor.Value;
            SetPadding(button, 6f, 12f);
            SetBorderRadius(button, 4f);
            return button;
        }

        /// <summary>Create a color indicator box (for country colors, etc.).</summary>
        public static VisualElement CreateColorIndicator(Color color, float size = 16f, float borderRadius = 2f)
        {
            var indicator = new VisualElement();
            SetSize(indicator, size, size);
            indicator.style.backgroundColor = color;
            SetBorderRadius(indicator, borderRadius);
            return indicator;
        }

        // ====================================================================
        // HOVER EFFECTS
        // ====================================================================

        /// <summary>Add hover color effect to an element.</summary>
        public static void AddHoverEffect(VisualElement element, Color normalColor, Color hoverColor)
        {
            element.style.backgroundColor = normalColor;
            element.RegisterCallback<MouseEnterEvent>(evt => element.style.backgroundColor = hoverColor);
            element.RegisterCallback<MouseLeaveEvent>(evt => element.style.backgroundColor = normalColor);
        }
    }
}
