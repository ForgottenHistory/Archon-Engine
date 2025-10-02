using System.IO;
using Core.Registries;
using Utils;
using UnityEngine;

namespace Core.Loaders
{
    /// <summary>
    /// Loads trade good definitions from data files
    /// Trade goods are static data with no dependencies on other entities
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public static class TradeGoodLoader
    {
        /// <summary>
        /// Load all trade goods from common/trade_goods directory
        /// </summary>
        public static void LoadTradeGoods(Registry<TradeGoodData> tradeGoodRegistry, string dataPath)
        {
            string tradeGoodsPath = Path.Combine(dataPath, "common", "trade_goods");

            if (!Directory.Exists(tradeGoodsPath))
            {
                DominionLogger.LogWarning($"Trade goods directory not found: {tradeGoodsPath}");
                CreateDefaultTradeGoods(tradeGoodRegistry);
                return;
            }

            var tradeGoodFiles = Directory.GetFiles(tradeGoodsPath, "*.txt");
            DominionLogger.Log($"TradeGoodLoader: Found {tradeGoodFiles.Length} trade good files in {tradeGoodsPath}");

            int loaded = 0;
            foreach (var file in tradeGoodFiles)
            {
                try
                {
                    LoadTradeGoodFile(tradeGoodRegistry, file);
                    loaded++;
                }
                catch (System.Exception e)
                {
                    DominionLogger.LogError($"TradeGoodLoader: Failed to load {file}: {e.Message}");
                }
            }

            DominionLogger.Log($"TradeGoodLoader: Loaded {loaded}/{tradeGoodFiles.Length} trade good files, {tradeGoodRegistry.Count} trade goods registered");

            // If no trade goods loaded, create defaults
            if (tradeGoodRegistry.Count == 0)
            {
                DominionLogger.LogWarning("TradeGoodLoader: No trade goods loaded, creating defaults");
                CreateDefaultTradeGoods(tradeGoodRegistry);
            }
        }

        /// <summary>
        /// Load trade goods from a single file
        /// </summary>
        private static void LoadTradeGoodFile(Registry<TradeGoodData> tradeGoodRegistry, string filePath)
        {
            var content = File.ReadAllText(filePath);

            // For now, create simple trade good entries
            // TODO: In a full implementation, this would parse the actual trade good file format
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Create basic trade goods based on common EU4/game types
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "grain", "Grain", 2.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "livestock", "Livestock", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "fish", "Fish", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "salt", "Salt", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "wine", "Wine", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "wool", "Wool", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "cloth", "Cloth", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "iron", "Iron", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "copper", "Copper", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "gold", "Gold", 4.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "silver", "Silver", 4.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "gems", "Gems", 4.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "ivory", "Ivory", 4.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "furs", "Furs", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "naval_supplies", "Naval Supplies", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "spices", "Spices", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "coffee", "Coffee", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "cotton", "Cotton", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "sugar", "Sugar", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "tobacco", "Tobacco", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "tea", "Tea", 2.5f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "chinaware", "Chinaware", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "silk", "Silk", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "dyes", "Dyes", 3.0f);
            RegisterTradeGoodIfNotExists(tradeGoodRegistry, "tropical_wood", "Tropical Wood", 2.5f);
        }

        /// <summary>
        /// Register a trade good if it doesn't already exist
        /// </summary>
        private static void RegisterTradeGoodIfNotExists(Registry<TradeGoodData> tradeGoodRegistry, string key, string name, float basePrice)
        {
            if (!tradeGoodRegistry.Exists(key))
            {
                var tradeGood = new TradeGoodData
                {
                    Name = name,
                    BasePrice = basePrice
                };

                tradeGoodRegistry.Register(key, tradeGood);
            }
        }

        /// <summary>
        /// Create default trade goods if no data files found
        /// Ensures the game can run even without complete data
        /// </summary>
        private static void CreateDefaultTradeGoods(Registry<TradeGoodData> tradeGoodRegistry)
        {
            var defaultTradeGoods = new[]
            {
                ("grain", "Grain", 2.0f),
                ("livestock", "Livestock", 2.5f),
                ("fish", "Fish", 2.5f),
                ("salt", "Salt", 3.0f),
                ("wine", "Wine", 2.5f),
                ("wool", "Wool", 2.5f),
                ("cloth", "Cloth", 3.0f),
                ("iron", "Iron", 3.0f),
                ("copper", "Copper", 3.0f),
                ("gold", "Gold", 4.0f),
                ("silver", "Silver", 4.0f),
                ("gems", "Gems", 4.0f),
                ("ivory", "Ivory", 4.0f),
                ("furs", "Furs", 3.0f),
                ("spices", "Spices", 3.0f),
                ("silk", "Silk", 3.0f)
            };

            foreach (var (key, name, basePrice) in defaultTradeGoods)
            {
                var tradeGood = new TradeGoodData
                {
                    Name = name,
                    BasePrice = basePrice
                };

                tradeGoodRegistry.Register(key, tradeGood);
            }

            DominionLogger.Log($"TradeGoodLoader: Created {defaultTradeGoods.Length} default trade goods");
        }
    }
}