using UnityEngine;
using Core;

namespace StarterKit
{
    /// <summary>
    /// Simple player state storage for StarterKit.
    /// Tracks which country the player controls.
    /// </summary>
    public class PlayerState
    {
        private readonly GameState gameState;
        private readonly bool logStateChanges;
        private ushort playerCountryId;

        public ushort PlayerCountryId => playerCountryId;
        public bool HasPlayerCountry => playerCountryId != 0;

        public PlayerState(GameState gameStateRef, bool log = true)
        {
            gameState = gameStateRef;
            logStateChanges = log;

            if (logStateChanges)
            {
                ArchonLogger.Log("PlayerState: Initialized", "starter_kit");
            }
        }

        public void SetPlayerCountry(ushort countryId)
        {
            if (countryId == 0)
            {
                ArchonLogger.LogWarning("PlayerState: Cannot set player country to 0", "starter_kit");
                return;
            }

            playerCountryId = countryId;

            if (logStateChanges)
            {
                string tag = gameState?.CountryQueries?.GetTag(countryId) ?? countryId.ToString();
                ArchonLogger.Log($"PlayerState: Player country set to {tag} (ID: {countryId})", "starter_kit");
            }
        }

        public void ClearPlayerCountry()
        {
            if (logStateChanges && playerCountryId != 0)
            {
                ArchonLogger.Log("PlayerState: Player country cleared", "starter_kit");
            }
            playerCountryId = 0;
        }

        public string GetPlayerCountryTag()
        {
            if (!HasPlayerCountry) return "NONE";
            return gameState.CountryQueries.GetTag(playerCountryId);
        }

        public Color32 GetPlayerCountryColor()
        {
            if (!HasPlayerCountry)
            {
                return new Color32(128, 128, 128, 255);
            }
            return gameState.CountryQueries.GetColor(playerCountryId);
        }

        public bool IsPlayerCountry(ushort countryId)
        {
            return HasPlayerCountry && countryId == playerCountryId;
        }
    }
}
