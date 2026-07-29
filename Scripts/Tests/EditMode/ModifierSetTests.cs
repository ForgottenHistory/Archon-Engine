using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Core.Data;
using Core.Modifiers;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for ModifierSet - fixed-size unsafe storage for 512 modifier types.
    ///
    /// WHY THIS MATTERS: Clear() takes a pointer to the first fixed buffer and clears
    /// past its end, relying on the next two buffers being laid out immediately after.
    /// That is not guaranteed by the CLR, only very likely, so the layout is pinned here
    /// rather than assumed. Everything else is bitmask bookkeeping, where an error means
    /// stale modifiers silently leak between scopes.
    /// </summary>
    public class ModifierSetTests
    {
        private static FixedPoint64 Fixed(int value) => FixedPoint64.FromInt(value);

        // ===== Layout (F4) =====

        /// <summary>
        /// Clear() writes 512*8*2 + 8*8 bytes starting at the additive buffer, which is
        /// only in bounds if the three fixed buffers are contiguous with no padding.
        /// If this assertion ever fails, Clear() is writing out of bounds and must be
        /// split into three separate fixed blocks.
        /// </summary>
        [Test]
        public void Layout_IsContiguousWithNoPadding()
        {
            int expected = ModifierSet.MAX_MODIFIER_TYPES * sizeof(long) * 2  // additive + multiplicative
                         + (ModifierSet.MAX_MODIFIER_TYPES / 64) * sizeof(long); // bitmask

            Assert.AreEqual(expected, UnsafeUtility.SizeOf<ModifierSet>(),
                "ModifierSet size changed. Clear() clears past the end of the additive " +
                "buffer assuming the other buffers follow immediately - if padding has " +
                "appeared, Clear() is now an out-of-bounds write.");
        }

        /// <summary>
        /// Behavioural counterpart to the size assertion: fill every region, clear, and
        /// confirm all three actually came back zero.
        /// </summary>
        [Test]
        public void Clear_ZeroesAllThreeRegions()
        {
            var set = new ModifierSet();

            for (ushort i = 0; i < ModifierSet.MAX_MODIFIER_TYPES; i++)
            {
                set.Add(i, Fixed(i + 1), false);
                set.Add(i, Fixed(i + 1), true);
            }

            Assert.IsTrue(set.HasActiveTypes, "Precondition: the set should be populated");

            set.Clear();

            for (ushort i = 0; i < ModifierSet.MAX_MODIFIER_TYPES; i++)
            {
                var value = set.Get(i);
                Assert.AreEqual(0L, value.Additive.RawValue, $"additive[{i}] not cleared");
                Assert.AreEqual(0L, value.Multiplicative.RawValue, $"multiplicative[{i}] not cleared");
            }

            Assert.IsFalse(set.HasActiveTypes, "The bitmask region must be cleared too");
        }

        // ===== Add / Get / Set / Remove =====

        [Test]
        public void Add_AccumulatesRatherThanReplacing()
        {
            var set = new ModifierSet();

            set.Add(5, Fixed(10), false);
            set.Add(5, Fixed(15), false);

            Assert.AreEqual(Fixed(25).RawValue, set.Get(5).Additive.RawValue,
                "Add must stack with the existing value");
        }

        [Test]
        public void Add_KeepsAdditiveAndMultiplicativeSeparate()
        {
            var set = new ModifierSet();

            set.Add(7, Fixed(10), false);
            set.Add(7, Fixed(3), true);

            var value = set.Get(7);
            Assert.AreEqual(Fixed(10).RawValue, value.Additive.RawValue);
            Assert.AreEqual(Fixed(3).RawValue, value.Multiplicative.RawValue);
        }

        [Test]
        public void Set_ReplacesRatherThanAccumulating()
        {
            var set = new ModifierSet();

            set.Add(9, Fixed(100), false);
            set.Set(9, Fixed(42), false);

            Assert.AreEqual(Fixed(42).RawValue, set.Get(9).Additive.RawValue,
                "Set must overwrite the existing value");
        }

        [Test]
        public void Remove_SubtractsTheValue()
        {
            var set = new ModifierSet();

            set.Add(3, Fixed(50), false);
            set.Remove(3, Fixed(20), false);

            Assert.AreEqual(Fixed(30).RawValue, set.Get(3).Additive.RawValue);
        }

        [Test]
        public void Remove_CanProduceNegativeValues()
        {
            var set = new ModifierSet();

            set.Remove(4, Fixed(10), false);

            Assert.AreEqual(Fixed(-10).RawValue, set.Get(4).Additive.RawValue,
                "Modifiers are signed - removing from nothing yields a penalty, not a clamp");
        }

        [Test]
        public void OutOfRangeTypeId_IsIgnoredRatherThanCorrupting()
        {
            var set = new ModifierSet();
            const ushort outOfRange = ModifierSet.MAX_MODIFIER_TYPES;

            Assert.DoesNotThrow(() =>
            {
                set.Add(outOfRange, Fixed(5), false);
                set.Set(outOfRange, Fixed(5), false);
                set.Remove(outOfRange, Fixed(5), false);
            }, "Out-of-range writes must be dropped, not written past the buffer");

            Assert.AreEqual(0L, set.Get(outOfRange).Additive.RawValue,
                "Out-of-range reads must return a default value");
            Assert.IsFalse(set.HasActiveTypes,
                "An out-of-range write must not mark anything active");
        }

        [Test]
        public void BoundaryTypeIds_AreUsable()
        {
            var set = new ModifierSet();
            const ushort last = ModifierSet.MAX_MODIFIER_TYPES - 1;

            set.Add(0, Fixed(11), false);
            set.Add(last, Fixed(22), false);

            Assert.AreEqual(Fixed(11).RawValue, set.Get(0).Additive.RawValue, "first slot");
            Assert.AreEqual(Fixed(22).RawValue, set.Get(last).Additive.RawValue, "last slot");
        }

        // ===== ApplyModifier =====

        [Test]
        public void ApplyModifier_UsesAdditiveThenMultiplicative()
        {
            var set = new ModifierSet();
            set.Add(1, Fixed(5), false);
            set.Add(1, FixedPoint64.FromFraction(1, 2), true);

            // (10 + 5) * (1 + 0.5) = 22.5
            var result = set.ApplyModifier(1, Fixed(10));

            Assert.AreEqual(FixedPoint64.FromFraction(45, 2).RawValue, result.RawValue,
                "Formula must be (base + additive) * (1 + multiplicative)");
        }

        [Test]
        public void ApplyModifier_WithNoModifiers_ReturnsBaseUnchanged()
        {
            var set = new ModifierSet();

            Assert.AreEqual(Fixed(10).RawValue, set.ApplyModifier(1, Fixed(10)).RawValue);
        }

        [Test]
        public void ApplyModifier_WithOnlyAdditive_SkipsMultiplication()
        {
            var set = new ModifierSet();
            set.Add(2, Fixed(5), false);

            Assert.AreEqual(Fixed(15).RawValue, set.ApplyModifier(2, Fixed(10)).RawValue);
        }

        [Test]
        public void ApplyModifier_WithOutOfRangeId_ReturnsBaseUnchanged()
        {
            var set = new ModifierSet();

            Assert.AreEqual(Fixed(10).RawValue,
                set.ApplyModifier(ModifierSet.MAX_MODIFIER_TYPES, Fixed(10)).RawValue);
        }

        // ===== Bitmask iteration =====

        /// <summary>
        /// ClearActive walks the bitmask instead of touching all 512 slots. It must leave
        /// the set in exactly the state Clear() would - if it misses a slot, a stale
        /// modifier leaks into the next rebuild.
        /// </summary>
        [Test]
        public void ClearActive_IsEquivalentToClear()
        {
            var viaClearActive = new ModifierSet();
            var viaClear = new ModifierSet();

            // Spread across word boundaries to exercise the bitmask walk.
            ushort[] ids = { 0, 1, 63, 64, 65, 127, 128, 255, 256, 511 };
            foreach (var id in ids)
            {
                viaClearActive.Add(id, Fixed(id + 1), false);
                viaClearActive.Add(id, Fixed(id + 2), true);
                viaClear.Add(id, Fixed(id + 1), false);
                viaClear.Add(id, Fixed(id + 2), true);
            }

            viaClearActive.ClearActive();
            viaClear.Clear();

            for (ushort i = 0; i < ModifierSet.MAX_MODIFIER_TYPES; i++)
            {
                Assert.AreEqual(viaClear.Get(i).Additive.RawValue,
                    viaClearActive.Get(i).Additive.RawValue,
                    $"additive[{i}] differs between ClearActive and Clear");
                Assert.AreEqual(viaClear.Get(i).Multiplicative.RawValue,
                    viaClearActive.Get(i).Multiplicative.RawValue,
                    $"multiplicative[{i}] differs between ClearActive and Clear");
            }

            Assert.IsFalse(viaClearActive.HasActiveTypes,
                "ClearActive must also reset the bitmask");
        }

        [Test]
        public void ClearActive_OnEmptySet_IsSafe()
        {
            var set = new ModifierSet();

            Assert.DoesNotThrow(() => set.ClearActive());
            Assert.IsFalse(set.HasActiveTypes);
        }

        [Test]
        public void HasActiveTypes_IsFalseOnFreshSet()
        {
            var set = new ModifierSet();

            Assert.IsFalse(set.HasActiveTypes);
        }

        [Test]
        public void HasActiveTypes_BecomesTrueAfterAdd()
        {
            var set = new ModifierSet();
            set.Add(100, Fixed(1), false);

            Assert.IsTrue(set.HasActiveTypes);
        }

        /// <summary>
        /// PINNED BEHAVIOUR (finding F5): Remove deliberately does not clear the bitmask,
        /// so a type whose value has returned to zero stays flagged active. HasActiveTypes
        /// therefore means "has touched slots", not "has non-zero values" - despite the
        /// name.
        ///
        /// This is currently harmless because CopyActiveToSet re-checks for non-zero, but
        /// the divergence is undocumented and a future caller could reasonably trust the
        /// name. Pinned so a change is a deliberate decision.
        /// </summary>
        [Test]
        public void HasActiveTypes_StaysTrueAfterRemovingBackToZero()
        {
            var set = new ModifierSet();

            set.Add(10, Fixed(5), false);
            set.Remove(10, Fixed(5), false);

            Assert.AreEqual(0L, set.Get(10).Additive.RawValue,
                "Precondition: the value is back to zero");
            Assert.IsTrue(set.HasActiveTypes,
                "Remove does not clear the bitmask, so the slot stays flagged. If this " +
                "now returns false, the bitmask semantics changed - verify CopyActiveToSet " +
                "and ClearActive still agree with Clear.");
        }

        // ===== CopyActiveToSet =====

        [Test]
        public void CopyActiveToSet_TransfersAllActiveValues()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();

            ushort[] ids = { 0, 63, 64, 200, 511 };
            foreach (var id in ids)
            {
                source.Add(id, Fixed(id + 1), false);
                source.Add(id, Fixed(id + 2), true);
            }

            source.CopyActiveToSet(ref target);

            foreach (var id in ids)
            {
                Assert.AreEqual(Fixed(id + 1).RawValue, target.Get(id).Additive.RawValue,
                    $"additive[{id}] was not copied");
                Assert.AreEqual(Fixed(id + 2).RawValue, target.Get(id).Multiplicative.RawValue,
                    $"multiplicative[{id}] was not copied");
            }
        }

        [Test]
        public void CopyActiveToSet_StacksOntoExistingTargetValues()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();

            source.Add(5, Fixed(10), false);
            target.Add(5, Fixed(7), false);

            source.CopyActiveToSet(ref target);

            Assert.AreEqual(Fixed(17).RawValue, target.Get(5).Additive.RawValue,
                "Copy uses Add, so parent values stack onto local ones");
        }

        [Test]
        public void CopyActiveToSet_LeavesUntouchedSlotsAlone()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();

            source.Add(5, Fixed(10), false);
            target.Add(6, Fixed(99), false);

            source.CopyActiveToSet(ref target);

            Assert.AreEqual(Fixed(99).RawValue, target.Get(6).Additive.RawValue,
                "Slots the source never set must be preserved in the target");
        }

        [Test]
        public void CopyActiveToSet_FromEmptySource_LeavesTargetUnchanged()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();
            target.Add(1, Fixed(5), false);

            source.CopyActiveToSet(ref target);

            Assert.AreEqual(Fixed(5).RawValue, target.Get(1).Additive.RawValue);
        }

        /// <summary>
        /// A slot flagged active but holding zero (see F5) must not be copied - otherwise
        /// it would mark the target active for a modifier that does not exist.
        /// </summary>
        [Test]
        public void CopyActiveToSet_SkipsFlaggedButZeroSlots()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();

            source.Add(10, Fixed(5), false);
            source.Remove(10, Fixed(5), false);

            source.CopyActiveToSet(ref target);

            Assert.AreEqual(0L, target.Get(10).Additive.RawValue,
                "A zero-valued slot must not be copied");
            Assert.IsFalse(target.HasActiveTypes,
                "Copying a zero-valued slot must not flag the target as active");
        }

        [Test]
        public void CopyActiveToSet_AcrossAllWordBoundaries()
        {
            var source = new ModifierSet();
            var target = new ModifierSet();

            // One id in each of the 8 bitmask words.
            for (int word = 0; word < ModifierSet.MAX_MODIFIER_TYPES / 64; word++)
            {
                var id = (ushort)(word * 64 + word);
                source.Add(id, Fixed(word + 1), false);
            }

            source.CopyActiveToSet(ref target);

            for (int word = 0; word < ModifierSet.MAX_MODIFIER_TYPES / 64; word++)
            {
                var id = (ushort)(word * 64 + word);
                Assert.AreEqual(Fixed(word + 1).RawValue, target.Get(id).Additive.RawValue,
                    $"Bitmask word {word} was not walked correctly");
            }
        }

        // ===== ModifierValue =====

        [Test]
        public void ModifierValue_Apply_UsesAdditiveThenMultiplicative()
        {
            var modifier = new ModifierValue
            {
                Additive = Fixed(5),
                Multiplicative = FixedPoint64.FromFraction(1, 2)
            };

            // (10 + 5) * 1.5 = 22.5
            Assert.AreEqual(FixedPoint64.FromFraction(45, 2).RawValue,
                modifier.Apply(Fixed(10)).RawValue);
        }

        [Test]
        public void ModifierValue_Apply_WithEmptyModifier_ReturnsBase()
        {
            var modifier = new ModifierValue();

            Assert.AreEqual(Fixed(10).RawValue, modifier.Apply(Fixed(10)).RawValue);
        }

        [Test]
        public void ModifierValue_Addition_CombinesBothComponents()
        {
            var a = new ModifierValue { Additive = Fixed(5), Multiplicative = Fixed(1) };
            var b = new ModifierValue { Additive = Fixed(3), Multiplicative = Fixed(2) };

            var combined = a + b;

            Assert.AreEqual(Fixed(8).RawValue, combined.Additive.RawValue);
            Assert.AreEqual(Fixed(3).RawValue, combined.Multiplicative.RawValue);
        }

        [Test]
        public void ModifierValue_Apply_WithNegativeMultiplicative_ReducesValue()
        {
            var modifier = new ModifierValue
            {
                Additive = FixedPoint64.Zero,
                Multiplicative = FixedPoint64.FromFraction(-1, 2)
            };

            // 10 * (1 - 0.5) = 5
            Assert.AreEqual(Fixed(5).RawValue, modifier.Apply(Fixed(10)).RawValue);
        }
    }
}
