using System.Collections.Generic;
using Core.Data;
using Unity.Collections;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Manages treaties between countries
    ///
    /// RESPONSIBILITY:
    /// - Treaty queries (AreAllied, HasNonAggressionPact, IsGuaranteeing, HasMilitaryAccess)
    /// - Treaty state changes (Form/Break Alliance, NAP, Guarantee, Military Access)
    /// - Treaty relationship queries (GetAllies, GetAlliesRecursive, GetGuaranteeing, GetGuaranteedBy)
    ///
    /// PATTERN: Stateless manager (receives data references from DiplomacySystem)
    /// - Does NOT own NativeCollections (passed as parameters)
    /// - Uses RelationData.treatyFlags bitfield for treaty storage
    /// - Directional treaties (Guarantee, Military Access) use separate bits for each direction
    ///
    /// PERFORMANCE:
    /// - Treaty checks: O(1) dictionary lookup + bitfield check
    /// - GetAllies: O(n) where n = total relationships
    /// - GetAlliesRecursive: O(n) BFS traversal
    /// </summary>
    public static class DiplomacyTreatyManager
    {
        // ========== TREATY QUERIES ==========

        /// <summary>
        /// Check if two countries have an alliance
        /// </summary>
        public static bool AreAllied(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var rel)) return false;
            return (rel.treatyFlags & (byte)TreatyFlags.Alliance) != 0;
        }

        /// <summary>
        /// Check if two countries have a non-aggression pact
        /// </summary>
        public static bool HasNonAggressionPact(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var rel)) return false;
            return (rel.treatyFlags & (byte)TreatyFlags.NonAggressionPact) != 0;
        }

        /// <summary>
        /// Check if guarantor country guarantees guaranteed country's independence
        /// Directional check
        /// </summary>
        public static bool IsGuaranteeing(
            ushort guarantor,
            ushort guaranteed,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(guarantor, guaranteed);
            if (!relations.TryGetValue(key, out var rel)) return false;

            // Check direction
            var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
            if (guarantor == c1)
                return (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0;
            else
                return (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0;
        }

        /// <summary>
        /// Check if granter grants military access to recipient
        /// Directional check
        /// </summary>
        public static bool HasMilitaryAccess(
            ushort granter,
            ushort recipient,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(granter, recipient);
            if (!relations.TryGetValue(key, out var rel)) return false;

            // Check direction
            var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
            if (granter == c1)
                return (rel.treatyFlags & (byte)TreatyFlags.MilitaryAccessFrom1To2) != 0;
            else
                return (rel.treatyFlags & (byte)TreatyFlags.MilitaryAccessFrom2To1) != 0;
        }

        // ========== TREATY RELATIONSHIP QUERIES ==========

        /// <summary>
        /// Get all allies of a country
        /// Returns list of country IDs that have alliance with given country
        /// </summary>
        public static List<ushort> GetAllies(
            ushort countryID,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var result = new List<ushort>();
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var rel = kvps.Values[i];

                if (!rel.InvolvesCountry(countryID)) continue;
                if ((rel.treatyFlags & (byte)TreatyFlags.Alliance) == 0) continue;

                result.Add(rel.GetOtherCountry(countryID));
            }

            kvps.Dispose();
            return result;
        }

        /// <summary>
        /// Get all allies recursively (alliance chain A→B→C)
        /// Uses BFS traversal to find all connected allies
        /// CRITICAL for war declaration validation and auto-join
        /// </summary>
        public static HashSet<ushort> GetAlliesRecursive(
            ushort countryID,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var visited = new HashSet<ushort>();
            var toVisit = new Queue<ushort>();

            toVisit.Enqueue(countryID);
            visited.Add(countryID);

            while (toVisit.Count > 0)
            {
                ushort current = toVisit.Dequeue();
                var allies = GetAllies(current, relations);

                foreach (var ally in allies)
                {
                    if (!visited.Contains(ally))
                    {
                        visited.Add(ally);
                        toVisit.Enqueue(ally);
                    }
                }
            }

            visited.Remove(countryID);  // Don't include self
            return visited;
        }

        /// <summary>
        /// Get all countries that this country guarantees
        /// </summary>
        public static List<ushort> GetGuaranteeing(
            ushort guarantorID,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var result = new List<ushort>();
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var key = kvps.Keys[i];
                var rel = kvps.Values[i];
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);

                if (!rel.InvolvesCountry(guarantorID)) continue;

                // Check if guarantorID is guaranteeing the other country
                if (guarantorID == c1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(c2);
                }
                else if (guarantorID == c2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(c1);
                }
            }

            kvps.Dispose();
            return result;
        }

        /// <summary>
        /// Get all countries that guarantee this country
        /// </summary>
        public static List<ushort> GetGuaranteedBy(
            ushort guaranteedID,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var result = new List<ushort>();
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var key = kvps.Keys[i];
                var rel = kvps.Values[i];
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);

                if (!rel.InvolvesCountry(guaranteedID)) continue;

                // Check if the other country is guaranteeing guaranteedID
                if (guaranteedID == c2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(c1);
                }
                else if (guaranteedID == c1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(c2);
                }
            }

            kvps.Dispose();
            return result;
        }

        // ========== TREATY STATE CHANGES ==========

        /// <summary>
        /// Form alliance between two countries
        /// Called by FormAllianceCommand after validation
        /// </summary>
        public static void FormAlliance(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DiplomacyRelationManager.DEFAULT_BASE_OPINION);
            }

            // Set alliance flag
            var rel = relations[key];
            rel.treatyFlags |= (byte)TreatyFlags.Alliance;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"Alliance formed: {country1} and {country2}");
        }

        /// <summary>
        /// Break alliance between two countries
        /// </summary>
        public static void BreakAlliance(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            rel.treatyFlags &= (byte)~TreatyFlags.Alliance;  // Clear alliance bit
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"Alliance broken: {country1} and {country2}");
        }

        /// <summary>
        /// Form non-aggression pact between two countries
        /// </summary>
        public static void FormNonAggressionPact(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DiplomacyRelationManager.DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            rel.treatyFlags |= (byte)TreatyFlags.NonAggressionPact;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"Non-aggression pact formed: {country1} and {country2}");
        }

        /// <summary>
        /// Break non-aggression pact between two countries
        /// </summary>
        public static void BreakNonAggressionPact(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            rel.treatyFlags &= (byte)~TreatyFlags.NonAggressionPact;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"Non-aggression pact broken: {country1} and {country2}");
        }

        /// <summary>
        /// Guarantor guarantees guaranteed country's independence (directional)
        /// </summary>
        public static void GuaranteeIndependence(
            ushort guarantor,
            ushort guaranteed,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(guarantor, guaranteed);

            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DiplomacyRelationManager.DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            var (country1, country2) = DiplomacyKeyHelper.UnpackKey(key);

            // Set directional guarantee bit
            if (guarantor == country1)
                rel.treatyFlags |= (byte)TreatyFlags.GuaranteeFrom1To2;
            else
                rel.treatyFlags |= (byte)TreatyFlags.GuaranteeFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"{guarantor} now guarantees {guaranteed}'s independence");
        }

        /// <summary>
        /// Revoke guarantee of independence
        /// </summary>
        public static void RevokeGuarantee(
            ushort guarantor,
            ushort guaranteed,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(guarantor, guaranteed);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);

            // Clear directional guarantee bit
            if (guarantor == c1)
                rel.treatyFlags &= (byte)~TreatyFlags.GuaranteeFrom1To2;
            else
                rel.treatyFlags &= (byte)~TreatyFlags.GuaranteeFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"{guarantor} revoked guarantee of {guaranteed}");
        }

        /// <summary>
        /// Grant military access (directional)
        /// </summary>
        public static void GrantMilitaryAccess(
            ushort granter,
            ushort recipient,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(granter, recipient);

            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DiplomacyRelationManager.DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            var (country1, country2) = DiplomacyKeyHelper.UnpackKey(key);

            // Set directional access bit
            if (granter == country1)
                rel.treatyFlags |= (byte)TreatyFlags.MilitaryAccessFrom1To2;
            else
                rel.treatyFlags |= (byte)TreatyFlags.MilitaryAccessFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"{granter} granted military access to {recipient}");
        }

        /// <summary>
        /// Revoke military access
        /// </summary>
        public static void RevokeMilitaryAccess(
            ushort granter,
            ushort recipient,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(granter, recipient);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);

            // Clear directional access bit
            if (granter == c1)
                rel.treatyFlags &= (byte)~TreatyFlags.MilitaryAccessFrom1To2;
            else
                rel.treatyFlags &= (byte)~TreatyFlags.MilitaryAccessFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"{granter} revoked military access from {recipient}");
        }
    }
}
