using UnityEngine;
using UnityEngine.UIElements;
using Core;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Province info presenter (stateless data formatting)
    /// Pattern: UI Presenter Pattern - Presenter Component
    ///
    /// Responsibilities:
    /// - Query GameState for province data
    /// - Format data for display
    /// - Update UI element contents
    /// </summary>
    public static class ProvinceInfoPresenter
    {
        /// <summary>
        /// Update province info labels with data from GameState
        /// </summary>
        public static void UpdatePanelData(
            ushort provinceID,
            GameState gameState,
            Label nameLabel,
            Label idLabel,
            VisualElement ownerColorIndicator,
            Label ownerLabel)
        {
            if (gameState == null || provinceID == 0)
                return;

            var provinceQueries = gameState.ProvinceQueries;
            var countryQueries = gameState.CountryQueries;

            // Province name (TODO: implement province name system)
            nameLabel.text = $"Province {provinceID}";

            // Province ID
            idLabel.text = $"ID: {provinceID}";

            // Get owner info
            ushort ownerID = provinceQueries.GetOwner(provinceID);
            if (ownerID != 0)
            {
                string ownerTag = countryQueries.GetTag(ownerID);
                Color32 ownerColor = countryQueries.GetColor(ownerID);

                ownerLabel.text = string.IsNullOrEmpty(ownerTag) ? $"Country {ownerID}" : ownerTag;
                ownerColorIndicator.style.backgroundColor = (Color)ownerColor;
                ownerColorIndicator.style.display = DisplayStyle.Flex;
            }
            else
            {
                ownerLabel.text = "Unowned";
                ownerColorIndicator.style.display = DisplayStyle.None;
            }
        }
    }
}
