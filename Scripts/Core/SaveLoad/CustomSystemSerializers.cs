using System.IO;
using Core.Systems;
using Core.Resources;
using Core.Data;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Custom serialization logic for specific systems
    ///
    /// Purpose:
    /// Some systems (TimeManager, ResourceSystem) have custom serialization needs
    /// that don't fit the generic SaveState/LoadState pattern.
    ///
    /// This class centralizes that custom logic, keeping SaveManager clean.
    ///
    /// Systems with generic SaveState/LoadState:
    /// - ProvinceSystem, ModifierSystem, CountrySystem, UnitSystem
    /// (These use SystemSerializer directly, no custom logic needed)
    ///
    /// Systems with custom serialization:
    /// - TimeManager (multiple fields, not a single SaveState method)
    /// - ResourceSystem (iterates all resources, special capacity handling)
    /// </summary>
    public static class CustomSystemSerializers
    {
        #region TimeManager

        /// <summary>
        /// Save TimeManager data
        /// Saves: tick, date/time, speed, pause state, accumulator
        /// </summary>
        public static void SaveTimeManager(BinaryWriter writer, TimeManager timeManager)
        {
            // Write tick counter
            writer.Write(timeManager.CurrentTick);

            // Write date/time
            writer.Write(timeManager.CurrentYear);
            writer.Write(timeManager.CurrentMonth);
            writer.Write(timeManager.CurrentDay);
            writer.Write(timeManager.CurrentHour);

            // Write speed and pause state
            writer.Write(timeManager.GameSpeed);
            writer.Write(timeManager.IsPaused);

            // Write accumulator
            SerializationHelper.WriteFixedPoint64(writer, timeManager.GetAccumulator());
        }

        /// <summary>
        /// Load TimeManager data
        /// </summary>
        public static void LoadTimeManager(BinaryReader reader, TimeManager timeManager)
        {
            // Read tick counter
            ulong tick = reader.ReadUInt64();

            // Read date/time
            int year = reader.ReadInt32();
            int month = reader.ReadInt32();
            int day = reader.ReadInt32();
            int hour = reader.ReadInt32();

            // Read speed and pause state
            int speed = reader.ReadInt32();
            bool paused = reader.ReadBoolean();

            // Read accumulator
            FixedPoint64 accumulator = SerializationHelper.ReadFixedPoint64(reader);

            // Restore state
            timeManager.LoadState(tick, year, month, day, hour, speed, paused, accumulator);
        }

        #endregion

        #region ResourceSystem

        /// <summary>
        /// Save ResourceSystem data
        /// Saves: maxCountries, resource storage arrays
        /// Skips: resourceDefinitions (will be re-registered by GAME layer on load)
        /// </summary>
        public static void SaveResourceSystem(BinaryWriter writer, ResourceSystem resourceSystem)
        {
            // Write capacity
            writer.Write(resourceSystem.MaxCountries);

            // Write resource count
            var resourceIds = new System.Collections.Generic.List<ushort>(resourceSystem.GetAllResourceIds());
            writer.Write(resourceIds.Count);

            // Write each resource's data
            foreach (ushort resourceId in resourceIds)
            {
                writer.Write(resourceId);

                // Write resource values for all countries
                for (int countryId = 0; countryId < resourceSystem.MaxCountries; countryId++)
                {
                    FixedPoint64 value = resourceSystem.GetResource((ushort)countryId, resourceId);
                    SerializationHelper.WriteFixedPoint64(writer, value);
                }
            }
        }

        /// <summary>
        /// Load ResourceSystem data
        /// Assumes ResourceSystem is already initialized with resource definitions from GAME layer
        /// </summary>
        public static void LoadResourceSystem(BinaryReader reader, ResourceSystem resourceSystem)
        {
            // Read capacity
            int savedMaxCountries = reader.ReadInt32();

            // Verify capacity matches (resources should already be initialized)
            if (savedMaxCountries != resourceSystem.MaxCountries)
            {
                ArchonLogger.LogWarning($"CustomSystemSerializers: Resource capacity mismatch (saved: {savedMaxCountries}, current: {resourceSystem.MaxCountries})", "core_saveload");
            }

            // Read resource count
            int resourceCount = reader.ReadInt32();

            // Read each resource's data
            for (int i = 0; i < resourceCount; i++)
            {
                ushort resourceId = reader.ReadUInt16();

                // Read resource values for all countries
                for (int countryId = 0; countryId < savedMaxCountries; countryId++)
                {
                    FixedPoint64 value = SerializationHelper.ReadFixedPoint64(reader);

                    // Only set if resource is registered and country ID is valid
                    if (countryId < resourceSystem.MaxCountries && resourceSystem.IsResourceRegistered(resourceId))
                    {
                        resourceSystem.SetResource((ushort)countryId, resourceId, value);
                    }
                }
            }
        }

        #endregion
    }
}
