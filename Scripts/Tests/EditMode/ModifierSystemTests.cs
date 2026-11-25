using NUnit.Framework;
using Core.Data;
using Core.Modifiers;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for ModifierSystem's country scope: lists that grow past their starting capacity, and the in-place
    /// (pointer) country query staying in step with adds, removals and global changes.
    ///
    /// WHY THIS MATTERS: a full list used to drop new modifiers with only a warning (a country with many researched
    /// technologies silently lost bonuses), and a query that reads the cached set in place must still see every change
    /// that marks the scope dirty.
    /// </summary>
    public class ModifierSystemTests
    {
        const ushort Speed = 3, Output = 7;

        static ModifierSource Tech(uint id, ushort type, int value) =>
            ModifierSource.CreatePermanent(ModifierSource.SourceType.Technology, id, type, FixedPoint64.FromInt(value), false);

        [Test]
        public void CountryList_GrowsPastStartingCapacity()
        {
            using var system = new ModifierSystem(maxCountries: 4, maxProvinces: 4);
            int count = ActiveModifierList.DEFAULT_CAPACITY * 3 + 5;
            for (uint i = 0; i < count; i++)
                Assert.IsTrue(system.AddCountryModifier(1, Tech(i, Speed, 1)), $"modifier {i} was dropped");

            Assert.AreEqual(FixedPoint64.FromInt(count), system.GetCountryModifier(1, Speed, FixedPoint64.Zero));

            // one from past the first growth: removing it takes exactly its value off
            uint deep = (uint)ActiveModifierList.DEFAULT_CAPACITY * 2 + 1;
            Assert.AreEqual(1, system.RemoveCountryModifiersBySource(1, ModifierSource.SourceType.Technology, deep));
            Assert.AreEqual(FixedPoint64.FromInt(count - 1), system.GetCountryModifier(1, Speed, FixedPoint64.Zero));
        }

        [Test]
        public void CountryQuery_FollowsAddsAndRemovals()
        {
            using var system = new ModifierSystem(maxCountries: 4, maxProvinces: 4);
            system.AddCountryModifier(2, Tech(10, Output, 5));
            Assert.AreEqual(FixedPoint64.FromInt(5), system.GetCountryModifier(2, Output, FixedPoint64.Zero));

            system.AddCountryModifier(2, Tech(11, Output, 2));
            Assert.AreEqual(FixedPoint64.FromInt(7), system.GetCountryModifier(2, Output, FixedPoint64.Zero));

            system.RemoveCountryModifiersBySource(2, ModifierSource.SourceType.Technology, 10);
            Assert.AreEqual(FixedPoint64.FromInt(2), system.GetCountryModifier(2, Output, FixedPoint64.Zero));

            // other countries are untouched
            Assert.AreEqual(FixedPoint64.Zero, system.GetCountryModifier(1, Output, FixedPoint64.Zero));
        }

        [Test]
        public void CountryQuery_InheritsGlobalChanges()
        {
            using var system = new ModifierSystem(maxCountries: 4, maxProvinces: 4);
            system.AddCountryModifier(1, Tech(1, Speed, 1));
            Assert.AreEqual(FixedPoint64.FromInt(1), system.GetCountryModifier(1, Speed, FixedPoint64.Zero));

            system.AddGlobalModifier(Tech(99, Speed, 10));
            Assert.AreEqual(FixedPoint64.FromInt(11), system.GetCountryModifier(1, Speed, FixedPoint64.Zero));

            system.RemoveGlobalModifiersBySource(ModifierSource.SourceType.Technology, 99);
            Assert.AreEqual(FixedPoint64.FromInt(1), system.GetCountryModifier(1, Speed, FixedPoint64.Zero));
        }
    }
}
