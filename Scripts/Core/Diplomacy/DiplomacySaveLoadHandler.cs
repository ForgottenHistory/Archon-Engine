using System.IO;
using Core.Data;
using Core.SaveLoad;
using Unity.Collections;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Handles diplomacy save/load serialization
    ///
    /// RESPONSIBILITY:
    /// - Save diplomatic state to binary format
    /// - Load diplomatic state and rebuild indices
    /// - Rebuild derived data (activeWars, warsByCountry, modifierCache)
    ///
    /// PATTERN: Stateless handler (receives data references from DiplomacySystem)
    /// - Does NOT own NativeCollections (passed as parameters)
    /// - Binary serialization for efficiency
    /// - Rebuilds indices after load (Pattern 14: Hybrid Save/Load)
    ///
    /// FORMAT:
    /// - relationCount: int32
    /// - For each relationship:
    ///   - country1, country2: ushort × 2
    ///   - baseOpinion: int64 (FixedPoint64.RawValue)
    ///   - atWar: bool
    ///   - treatyFlags: byte
    ///   - modifierCount: int32
    ///   - For each modifier:
    ///     - modifierTypeID: ushort
    ///     - value: int64 (FixedPoint64.RawValue)
    ///     - appliedTick: int32
    ///     - decayRate: int32
    /// </summary>
    public static class DiplomacySaveLoadHandler
    {
        /// <summary>
        /// Save diplomatic state to save data
        /// Pattern 14: Hybrid Save/Load (state snapshot for speed)
        /// </summary>
        public static void OnSave(
            SaveGameData saveData,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashSet<ulong> activeWars)
        {
            ArchonLogger.LogCoreDiplomacy("Saving diplomacy state...");

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // Get all relations
                var kvps = relations.GetKeyValueArrays(Allocator.Temp);

                // Write relationship count
                writer.Write(kvps.Keys.Length);

                // Write each relationship
                for (int i = 0; i < kvps.Keys.Length; i++)
                {
                    var key = kvps.Keys[i];
                    var relation = kvps.Values[i];
                    var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);

                    // Write key
                    writer.Write(c1);
                    writer.Write(c2);

                    // Write hot data
                    writer.Write(relation.baseOpinion.RawValue);
                    writer.Write(relation.atWar);
                    writer.Write(relation.treatyFlags);

                    // FLAT STORAGE: Count and write modifiers for this relationship
                    int modifierCount = 0;
                    for (int m = 0; m < allModifiers.Length; m++)
                    {
                        if (allModifiers[m].relationshipKey == key)
                            modifierCount++;
                    }

                    writer.Write(modifierCount);

                    // Write each modifier for this relationship
                    for (int m = 0; m < allModifiers.Length; m++)
                    {
                        if (allModifiers[m].relationshipKey == key)
                        {
                            var modifier = allModifiers[m].modifier;
                            writer.Write(modifier.modifierTypeID);
                            writer.Write(modifier.value.RawValue);
                            writer.Write(modifier.appliedTick);
                            writer.Write(modifier.decayRate);
                        }
                    }
                }

                kvps.Dispose();

                // Store in saveData
                saveData.systemData["Diplomacy"] = stream.ToArray();
            }

            ArchonLogger.LogCoreDiplomacy($"Saved {relations.Count()} relationships, {activeWars.Count()} wars");
        }

        /// <summary>
        /// Load diplomatic state from save data
        /// Rebuilds derived indices (activeWars, warsByCountry, modifierCache)
        /// </summary>
        public static void OnLoad(
            SaveGameData saveData,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeParallelHashSet<ulong> activeWars,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            ArchonLogger.LogCoreDiplomacy("Loading diplomacy state...");

            // Clear existing data
            relations.Clear();
            activeWars.Clear();
            warsByCountry.Clear();
            allModifiers.Clear();
            modifierCache.Clear();

            // Get saved data
            if (!saveData.systemData.ContainsKey("Diplomacy"))
            {
                ArchonLogger.LogCoreDiplomacyWarning("No save data found - starting fresh");
                return;
            }

            byte[] data = (byte[])saveData.systemData["Diplomacy"];
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                // Read relationship count
                int relationCount = reader.ReadInt32();

                // Read each relationship
                for (int i = 0; i < relationCount; i++)
                {
                    // Read key
                    ushort country1 = reader.ReadUInt16();
                    ushort country2 = reader.ReadUInt16();
                    var key = DiplomacyKeyHelper.GetKey(country1, country2);

                    // Read hot data
                    long baseOpinionRaw = reader.ReadInt64();
                    bool atWar = reader.ReadBoolean();
                    byte treatyFlags = reader.ReadByte();

                    // Create relation
                    var relation = new RelationData
                    {
                        country1 = country1,
                        country2 = country2,
                        baseOpinion = FixedPoint64.FromRaw(baseOpinionRaw),
                        atWar = atWar,
                        treatyFlags = treatyFlags
                    };

                    relations[key] = relation;

                    // Rebuild war indices if at war
                    if (atWar)
                    {
                        activeWars.Add(key);
                        warsByCountry.Add(country1, country2);
                        warsByCountry.Add(country2, country1);
                    }

                    // FLAT STORAGE: Read modifiers for this relationship
                    int modifierCount = reader.ReadInt32();

                    // Read each modifier and add to flat storage
                    for (int j = 0; j < modifierCount; j++)
                    {
                        var modifier = new OpinionModifier
                        {
                            modifierTypeID = reader.ReadUInt16(),
                            value = FixedPoint64.FromRaw(reader.ReadInt64()),
                            appliedTick = reader.ReadInt32(),
                            decayRate = reader.ReadInt32()
                        };

                        allModifiers.Add(new ModifierWithKey
                        {
                            relationshipKey = key,
                            modifier = modifier
                        });
                    }
                }
            }

            // Rebuild cache after loading all modifiers
            DiplomacyModifierProcessor.RebuildModifierCache(allModifiers, modifierCache);

            ArchonLogger.LogCoreDiplomacy($"Loaded {relations.Count()} relationships, {activeWars.Count()} wars, {allModifiers.Length} modifiers");
        }
    }
}
