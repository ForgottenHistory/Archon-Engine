using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Localization;

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

            // Province name - try localization first (PROV1, PROV2, etc.)
            string provinceName = LocalizationManager.Get($"PROV{provinceID}");
            nameLabel.text = provinceName;

            // Province ID
            idLabel.text = $"{LocalizationManager.Get("UI_PROVINCE_ID")}: {provinceID}";

            // Get owner info
            ushort ownerID = provinceQueries.GetOwner(provinceID);
            if (ownerID != 0)
            {
                string ownerTag = countryQueries.GetTag(ownerID);
                Color32 ownerColor = countryQueries.GetColor(ownerID);

                // Try to get localized country name, fallback to tag
                string countryName = LocalizationManager.Get(ownerTag);
                ownerLabel.text = countryName;
                ownerColorIndicator.style.backgroundColor = (Color)ownerColor;
                ownerColorIndicator.style.display = DisplayStyle.Flex;
            }
            else
            {
                ownerLabel.text = LocalizationManager.Get("UI_UNOWNED");
                ownerColorIndicator.style.display = DisplayStyle.None;
            }
        }
    }
}
