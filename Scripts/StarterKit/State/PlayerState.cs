using UnityEngine;
using System.IO;
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

        /// <summary>The country ID the player is currently controlling (0 if none selected).</summary>
        public ushort PlayerCountryId => playerCountryId;

        /// <summary>True if the player has selected a country to control.</summary>
        public bool HasPlayerCountry => playerCountryId != 0;

        /// <summary>
        /// Create a new PlayerState instance.
        /// </summary>
        /// <param name="gameStateRef">Reference to the game state.</param>
        /// <param name="log">Whether to log state changes.</param>
        public PlayerState(GameState gameStateRef, bool log = true)
        {
            gameState = gameStateRef;
            logStateChanges = log;

            if (logStateChanges)
            {
                ArchonLogger.Log("PlayerState: Initialized", "starter_kit");
            }
        }

        /// <summary>
        /// Set the player's controlled country.
        /// </summary>
        /// <param name="countryId">The country ID to control (must be non-zero).</param>
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

        /// <summary>
        /// Clear the player's country selection (sets to 0/none).
        /// </summary>
        public void ClearPlayerCountry()
        {
            if (logStateChanges && playerCountryId != 0)
            {
                ArchonLogger.Log("PlayerState: Player country cleared", "starter_kit");
            }
            playerCountryId = 0;
        }

        /// <summary>
        /// Get the 3-letter tag for the player's country.
        /// </summary>
        /// <returns>Country tag (e.g., "ROM") or "NONE" if no country selected.</returns>
        public string GetPlayerCountryTag()
        {
            if (!HasPlayerCountry) return "NONE";
            return gameState.CountryQueries.GetTag(playerCountryId);
        }

        /// <summary>
        /// Get the map color for the player's country.
        /// </summary>
        /// <returns>Country color, or gray if no country selected.</returns>
        public Color32 GetPlayerCountryColor()
        {
            if (!HasPlayerCountry)
            {
                return new Color32(128, 128, 128, 255);
            }
            return gameState.CountryQueries.GetColor(playerCountryId);
        }

        /// <summary>
        /// Check if a given country is the player's country.
        /// </summary>
        /// <param name="countryId">The country ID to check.</param>
        /// <returns>True if the player controls this country.</returns>
        public bool IsPlayerCountry(ushort countryId)
        {
            return HasPlayerCountry && countryId == playerCountryId;
        }

        // ====================================================================
        // SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Serialize player state to byte array
        /// </summary>
        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(playerCountryId);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize player state from byte array
        /// </summary>
        public void Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                playerCountryId = reader.ReadUInt16();

                if (logStateChanges)
                {
                    string tag = gameState?.CountryQueries?.GetTag(playerCountryId) ?? playerCountryId.ToString();
                    ArchonLogger.Log($"PlayerState: Loaded player country {tag} (ID: {playerCountryId})", "starter_kit");
                }
            }
        }
    }
}
