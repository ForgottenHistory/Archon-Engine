# Core Test Coverage Plan (TEMPORARY WORKING DOC)

**Status:** Working document — delete once tests are implemented and findings are resolved.
**Scope:** `Assets/Archon-Engine/Scripts/Core/` — units testable in isolation only.
**Model:** `Assets/Archon-Engine/Scripts/Tests/EditMode/FixedPoint64Tests.cs`

**Constraint for this pass:** no file loading, no Unity scene, no GameObject, no MonoBehaviour.
Excludes Loaders, Modding, SaveLoad, Initialization phases, EngineInitializer, UI.

---

## Test Harness — Already Ready

`Scripts/Tests/Tests.asmdef` requires no changes. It already references `Core`, `Unity.Collections`,
`Unity.Mathematics`, `Unity.Burst`, `Unity.Jobs`, and sets `allowUnsafeCode: true` (needed for
`ModifierSet` and `ProvinceState` byte round-trips).

Place new files in `Scripts/Tests/EditMode/`, namespace `Tests.EditMode`.

---

## Verification Status Legend

- **CONFIRMED** — reproduced numerically outside Unity, exact expected-vs-actual known.
- **READ-ONLY** — identified by reading the source; not yet executed. Plausible, needs the test to confirm.
- **NOT A BUG** — investigated and cleared; recorded so it isn't re-raised later.

---

## Findings

### F1 — `FixedPoint32.Frac` is wrong for all negative non-integers — CONFIRMED

`Data/Math/FixedPointMath.cs:308-314`

```csharp
int fractional = value.RawValue & (ONE_RAW - 1);
if (value.RawValue < 0 && fractional != 0)
    fractional = ONE_RAW - fractional;   // <-- this branch breaks it
```

`Floor` uses an arithmetic shift, so it floors toward negative infinity. Under two's complement,
`RawValue & 0xFFFF` **already** yields the correct fractional part consistent with that `Floor`.
The correction branch then inverts a value that was already right.

Reproduced (16.16, value = raw / 65536):

| raw | value | Floor | Frac (current) | Floor+Frac (current) | Frac (mask only) | Floor+Frac (mask) |
|---|---|---|---|---|---|---|
| -16384 | -0.25 | -1.0 | 0.25 | **-0.75** | 0.75 | -0.25 |
| -81920 | -1.25 | -2.0 | 0.25 | **-1.75** | 0.75 | -1.25 |
| -1 | ~-0.0 | -1.0 | 0.0 | **-1.0** | 1.0 | -0.0 |
| 16384 | 0.25 | 0.0 | 0.25 | 0.25 | 0.25 | 0.25 |

Positive values are unaffected. The invariant `Floor(x) + Frac(x) == x` fails for every negative
non-integer.

**Fix:** delete the `if` branch; return `RawValue & (ONE_RAW - 1)` directly.
**Test:** property test `Floor(x) + Frac(x) == x` over a wide raw range including negatives, plus the
exact table above.
**Caveat:** `raw = -65535` (value -0.99998) also exposes it, but note the mask-only result there is
`0.0` with `Floor = -1.0`, summing to `-1.0` not `-0.99998` — that is a *separate* representational
rounding artifact of the table row, not a second bug. The property test should use exact raw
arithmetic, not float comparison, to avoid chasing this.

---

### F2 — `DeterministicRandom.NextFixed()` returns values ~32768x too large — READ-ONLY

`Data/DeterministicRandom.cs:146-151`

```csharp
public FixedPoint32 NextFixed()
{
    uint value = NextUInt();
    return FixedPoint32.FromRaw((int)(value >> 1)); // doc says range [0, 1)
}
```

`FixedPoint32` is 16.16, so `One` is raw `65536`. A 32-bit random shifted right by 1 produces raw
values up to `2^31 - 1`, i.e. a **value up to ~32768**, not `[0, 1)`. The shift needs to land the
result in `[0, 65536)`, which means `>> 16`.

**Blast radius** (all in `DeterministicRandom.cs`, no GAME-layer callers yet — grep of
`Assets/Game` returned zero hits, so this is contained to the engine and safe to fix now):
- `NextBool(FixedPoint32 probability)` (:185) — comparison almost always false; probability ignored.
- `NextPointInCircle` (:268) — rejection loop `while (lengthSquared > One)` will almost never accept.
  With `x`,`y` in the tens of thousands, this is effectively an infinite loop / hang.
- `NextGaussian` (:289) — see F3.
- `NextWeightedElement(T[], FixedPoint32[])` (:396) — `roll` overshoots `totalWeight`, so it always
  falls through to the final `return elements[elements.Length - 1]`.

`NextFixed(FixedPoint32 max)` (:156) is a **separate, correct** implementation
(`((long)NextUInt() * max.RawValue) >> 32`) — do not "fix" that one.

**Priority:** fix ahead of the tests. `NextPointInCircle` is a live hang risk.

---

### F3 — `NextGaussian` discards its own Box-Muller work and returns a wrong distribution — READ-ONLY

`Data/DeterministicRandom.cs:289-320`

Computes `u1Raw`, `u2Raw`, guards `u1Raw == 0`, builds `u1` and `u2`, comments out a full Box-Muller
derivation — then **uses none of it**, falling through to sum-of-12-uniforms minus 6.

Two consequences:
1. The two discarded `NextUInt()` draws still **advance the RNG stream**. Any change to this method
   shifts every subsequent random value — a determinism/save-compat hazard.
2. Because it sums `NextFixed()` (F2), the result is not a standard normal. With `NextFixed`
   returning ~[0, 32768), `sum - 6` lands on the order of ±10^5 instead of ±3.

Fixing F2 alone makes the CLT approach approximately correct (mean 0, stddev 1). The dead `u1`/`u2`
draws should be removed at the same time, accepting the one-time stream shift.

**Test:** after fix, assert mean and stddev over a large sample fall within tolerance, and that the
RNG stream position advances by exactly the expected number of draws.

---

### F4 — `ModifierSet.Clear()` writes past the end of the field it takes a pointer to — READ-ONLY

`Modifiers/ModifierSet.cs:107-113`

```csharp
fixed (long* add = additive)
    UnsafeUtility.MemClear(add, MAX_MODIFIER_TYPES * sizeof(long) * 2 + BITMASK_LONGS * sizeof(long));
```

Takes a pointer to `additive` (4096 bytes) and clears 8256 bytes — assuming `multiplicative` and
`activeTypeMask` are laid out immediately after with zero padding. `LayoutKind.Sequential` plus
`fixed` buffers makes this very likely in practice, but the CLR is not contractually obliged to
avoid padding between fixed buffers. If it is ever wrong, it is a silent out-of-bounds write into
adjacent memory.

**VERIFIED SAFE, NOT FIXED** — measured layout under `LayoutKind.Sequential`:

```
sizeof(ModifierSet) = 8256  (== 512*8*2 + 8*8, no padding)
offsets: additive=0, multiplicative=4096, activeTypeMask=8192
Clear() writes 8256 bytes from offset 0 -> exactly in bounds
```

The three buffers are contiguous, so `Clear()` is correct today. It remains *incidentally* safe
rather than guaranteed, which is why the size assertion is worth keeping as a tripwire — if a future
Unity/IL2CPP version introduces padding, `Clear()` silently becomes an out-of-bounds write and only
that assertion will catch it.

**Tests:** `Layout_IsContiguousWithNoPadding` (size tripwire), `Clear_ZeroesAllThreeRegions`
(behavioural counterpart).

Also verified: `BitCount.TrailingZeroCount` is correct for all edge cases — powers of two, the sign
bit (`1L << 63` → 63), `-1` → 0, and `0` → 64.

---

### F5 — `HasActiveTypes` can disagree with actual content after `Remove()` — READ-ONLY

`Modifiers/ModifierSet.cs:70-82` and `:167-180`

`Remove()` deliberately does not clear the bitmask (documented at :80). So `Add(id, v)` followed by
`Remove(id, v)` leaves the type flagged active with both values at zero. Then:
- `HasActiveTypes` returns `true`.
- `CopyActiveToSet` iterates the type but copies nothing (guards on `!= Zero`).

Not a correctness bug today, but it is an undocumented divergence that a future caller will trip on.

**PINNED, NOT CHANGED.** `HasActiveTypes` currently means "has touched slots", not "has non-zero
values" as the name implies. Confirmed harmless in the live call path: `ScopedModifierContainer`
uses `Clear()`/`ClearActive()` as the reset before `CopyActiveToSet`, and that copy re-checks for
non-zero, so a stale flag costs one skipped iteration and nothing more.

**Tests:** `HasActiveTypes_StaysTrueAfterRemovingBackToZero` (pins the divergence),
`CopyActiveToSet_SkipsFlaggedButZeroSlots` (pins the guard that makes it harmless),
`ClearActive_IsEquivalentToClear` (the invariant that actually matters — both are used as the reset
step, so they must agree).

**Open:** rename to `HasTouchedSlots`, or make `Remove` clear the bit when the value reaches zero.
Neither is urgent; the tests make either change safe to attempt.

---

### F6 — `FixedPoint32.Percentage` divides before multiplying, losing precision — READ-ONLY

`Data/Math/FixedPointMath.cs:319-325`

```csharp
return (value / total) * Hundred;
```

With only 16 fractional bits, computing the ratio first discards significant digits for small
ratios. `1 / 1000` is raw 65, and `* 100` gives raw 6553 (~0.0999) instead of 0.1 — and it degrades
fast below that. `(value * Hundred) / total` preserves far more precision.

**Caveat:** the reordered form has a larger intermediate and can overflow the 16.16 range for large
`value`. The multiply already widens to `long` internally, so this is safe for realistic inputs, but
the test should cover both a small-ratio precision case and a large-`value` overflow case.

---

### F7 — `FixedPoint32.Sqrt` fails to converge for inputs above ~900 — CONFIRMED, worse than first assessed

`Data/Math/FixedPointMath.cs:208-226`

`int guess = x >> 1` uses the **raw** value as the seed while the iteration operates in fixed-point
scale. Iteration count is hardcoded at 6 with no convergence test.

Originally logged as "likely poor at the extremes." Reproduction shows it is materially broken well
inside the normal operating range:

| input | returned | expected | error |
|---|---|---|---|
| 100 | 10.00000 | 10 | exact |
| 841 (29²) | 29.0086 | 29 | 0.03% |
| 900 (30²) | 30.0117 | 30 | 0.04% |
| 2500 (50²) | 50.5995 | 50 | 1.2% |
| 10000 | **116.75** | 100 | **16.75%** |

Accurate below ~29; degrades progressively above; unusable by 10000. Six Newton iterations cannot
converge from a raw-scaled seed at that magnitude.

**Impact:** any distance or magnitude calculation over ~900 squared units. `FixedPoint2.Length` and
`FixedPoint2.Distance` both route through this.

**Root cause — same bug, already fixed once elsewhere:** `FixedPoint64.Sqrt` carried the *identical*
defect and was previously repaired (its comment records `Sqrt(1000000)` converging to 2120 instead of
1000). `FixedPoint32.Sqrt` never received the same treatment. This is an argument for auditing the
two types side by side whenever either changes — they are near-duplicates that drift apart.

**FIXED** — ported the proven `FixedPoint64` approach: bit-length-derived initial guess
(`guessShift = (bitLength + FRACTIONAL_BITS) / 2`) plus 8 Newton iterations instead of 6.

Verified after fix:

| input | before | after | expected |
|---|---|---|---|
| 900 | 30.0117 | 30.00000 | 30 |
| 2500 | 50.5995 | 50.00000 | 50 |
| 10000 | 116.75 | 100.00000 | 100 |
| 32000 | 291.269 | 178.88544 | 178.88544 |

- Perfect squares 1–180: **exact** (worst error 0.00, was failing from n=30 up).
- Max relative error across a full sweep: 6.2e-4, occurring at raw=22 (value 0.0003) — at the floor
  of 16.16 precision, unavoidable.
- Monotonic across the range (matters for distance ordering).

**Tests:** `Sqrt_LargeInputs_ConvergeCorrectly` (regression), `Sqrt_MatchesReferenceAcrossRange`,
`Sqrt_OfPerfectSquares_IsNearExact` (now to n=180), `Sqrt_IsMonotonic`.

---

### F8 — `GameTime` round-trip and the year-0 model — NOT A BUG

`Systems/TimeManager.cs:478-541`, `Systems/StandardCalendar.cs:69-91`

Both halves of the concern were unfounded.

**Round-trip is exact,** including negative years. Verified `FromTotalHours(t.ToTotalHours()) == t`
across every day-and-hour of a full year, and across years 0, −1, −2, −100, −1000 — zero failures.

The apparent asymmetry (`FromTotalHours` handles negative remainders, `ToTotalHours` does not) is
correct as written: `Year * HOURS_PER_YEAR` is already correctly signed and the month/day offsets are
always positive, so no correction is needed on the forward path. Worth knowing, since it looks like
an omission and could easily get "fixed" into an actual bug.

**Year 0 is a deliberate convention,** not a mismatch. Year 0 is a real year on the arithmetic axis
and displays as "1 BC"; year −1 displays as "2 BC". That is the standard astronomical convention —
there is no year zero in BC/AD display. Self-consistent, now pinned by
`Calendar_FormatsYearsAcrossTheEraBoundary`.

Also verified incidentally: `DAYS_BEFORE_MONTH` is an exact prefix sum of `DAYS_IN_MONTH` summing to
365, and `GetHashCode`'s positional encoding produces no collisions across years −50..2000.

---

### F9 — `DiplomacyKeyHelper.UnpackKey` mask is wider than the packed field — READ-ONLY, low severity

`Diplomacy/DiplomacyKeyHelper.cs:34-39`

Packs `(country1 << 32) | country2` but unpacks the low half with `(ushort)(key & 0xFFFFFFFF)` —
masking 32 bits then truncating to 16. Correct in practice only because `country2` is a `ushort`.
The class comment claims a 64-bit key format when only 48 bits are meaningful, and the asymmetry
invites a future bug if the ID type ever widens.

**Test:** exhaustive-ish round-trip over representative pairs, plus the normalization invariant
(`GetKey(a,b) == GetKey(b,a)` and unpacked `country1 <= country2`).

---

### NOT A BUG — `NextUInt(uint max)` rejection sampling

`Data/DeterministicRandom.cs:105`

```csharp
uint threshold = (0xFFFFFFFFU - max + 1) % max;
```

I initially flagged this as a possible inversion. It is **correct**. `0xFFFFFFFFU - max + 1` wraps to
exactly `2^32 - max`, matching the canonical `(2^32) % max` threshold. Verified against arbitrary-
precision arithmetic:

| max | as written | canonical | match |
|---|---|---|---|
| 3 | 1 | 1 | yes |
| 10 | 6 | 6 | yes |
| 100 | 96 | 96 | yes |
| 1000 | 296 | 296 | yes |
| 1073741824 | 0 | 0 | yes |

Still deserves tests — it underpins `NextInt`, `NextPercent`, `Shuffle`, and all weighted selection —
but as characterization, not as a bug fix.

---

### NOT A BUG — `NativeMinHeap.Pop` complexity

`Collections/NativeMinHeap.cs:44-64`

`NativeList.RemoveAt` is O(n) in general, but both call sites remove from the **end** (`lastIndex`,
or index 0 of a single-element list), so no shifting occurs. The documented O(log n) holds.
Recorded so it is not "optimized" into `RemoveAtSwapBack` — which at index 0 would corrupt the heap.

---

## Implementation Order

### DONE — Fixes applied

1. **F2 fixed** — `NextFixed()` now shifts by 16, not 1. Verified: range `[0, 0.99998]`, mean
   0.50026 over 200k draws (was max 32767.93). `NextPointInCircle` now terminates in 1.28 average
   rejection iterations, matching the theoretical 4/π ≈ 1.27.
2. **F3 fixed** — dead Box-Muller draws removed; the method now consumes exactly 12 RNG draws.
   Verified: mean −0.0069, stddev 1.0018 over 200k samples. Documented the draw count as part of the
   determinism contract.
3. **F1 fixed** — `Frac` negative-correction branch deleted. Verified: `Floor(x) + Frac(x) == x`
   across 27,028 samples including negatives. **The identical bug was then found in
   `FixedPoint64.Frac`** and fixed there too — it had failed 10,847,246 of 21,694,494 sampled values
   (every negative input) and survived because the FP64 suite had no `Frac` coverage at all.
4. **F7 fixed** — `FixedPoint32.Sqrt` now uses a bit-length-derived seed + 8 iterations, ported from
   the already-fixed `FixedPoint64.Sqrt`. Perfect squares exact to n=180; `Sqrt(10000)` returns 100
   instead of 116.75. Defect-pinning test replaced with a regression test.

**Not yet compiled in Unity** — needs a build to confirm.

### DONE — Tests written

- `Tests/EditMode/FixedPoint32Tests.cs` — covers F1 (regression + invariant), F6, F7, plus
  multiplication/division against BigInteger, conversions, Lerp family, Pow, comparison, FixedPoint2.
- `Tests/EditMode/FixedPoint64Tests.cs` — extended with `Frac`/`Floor` coverage (was absent, which is
  why the shared F1 bug survived here).
- `Tests/EditMode/DeterministicRandomTests.cs` — golden sequences, state round-trip, branch
  independence, F2 range regression, F3 moments + draw-count contract, chi-square uniformity for
  `NextInt`/`NextFixed`, weighted selection, shuffle, seed phrases.

  All statistical thresholds were measured against the real algorithm before being asserted:
  `NextInt(100)` chi²=96.80 (crit 148.23), `NextInt(6)` chi²=4.12 (crit 20.52), `NextFixed` chi²=9.09
  (crit 27.88), gaussian mean 0.0015 / stddev 0.9999, `NextPointInCircle` 0/2000 outside radius at
  1.29 avg iterations.

  **Cost:** ~5M RNG draws total. Slower than the FixedPoint suites; reduce sample counts if it
  becomes a drag on the normal edit loop, at the price of weaker chi-square power.

- `Tests/EditMode/ModifierSetTests.cs` — F4 layout tripwire, F5 pinned semantics, Clear/ClearActive
  equivalence, bitmask walk across all 8 words, add/set/remove, out-of-range guards, apply formula.

- `Tests/EditMode/GameTimeTests.cs` — F8 round-trip (full year + negative years), monotonicity,
  calendar-constant consistency, Add/duration/comparison arithmetic, StandardCalendar formatting and
  clamping.

  **NUnit gotcha:** `GameTime` implements only the generic `IComparable<GameTime>`, so
  `Assert.Less`/`Assert.Greater`/`Is.LessThan` do not bind to it. Use the comparison operators
  directly. Applies to any future struct with the same shape.

- `Tests/EditMode/NativeMinHeapTests.cs` — ordering under randomised interleaved push/pop against a
  sorted reference, duplicates, sorted/reverse/negative input, capacity growth, empty-heap throws,
  `PathfindingNode` ordering by fScore including fractional costs.
- `Tests/EditMode/DiplomacyKeyHelperTests.cs` — F9 round-trip, order independence across 40×40 pairs,
  normalisation, boundary IDs, pair-uniqueness scan.
- `Tests/EditMode/ProvinceStateTests.cs` — 8-byte invariant, byte round-trip incl. extreme values,
  factory methods, `IsOwned`/`IsOccupied`/`IsOcean` edge cases, checksum stability.

**All planned tests for this pass are written.** Nothing remaining in scope.

### Cross-cutting observation

`FixedPoint32` and `FixedPoint64` are near-duplicate implementations that have now been found to
share the same bug **twice** (the multiplication limb decomposition, and the Sqrt initial guess).
When either type is touched, diff it against the other. Candidates not yet cross-checked:
`Pow`, `Frac`/`Floor`/`Ceiling` rounding behavior, and whether `FixedPoint64` has its own
`Percentage` with the F6 divide-first ordering.

Lower priority, cheap if wanted later: `Data/Ids/*`, `Common/Result.cs`, `Validation/Validate.cs`,
`Common/FrameCache.cs` (testable but reads `Time.frameCount`, which does not advance in EditMode —
frame-change behavior cannot be exercised without abstracting the frame source).

---

## Deliberately Out of Scope This Pass

Needs file I/O, scene setup, or a live `GameState`: Loaders, Modding, SaveLoad, Initialization
phases, `EngineInitializer`, `GameState`, `EventBus`, `CommandProcessor`, all Systems facades
(`ProvinceSystem`, `CountrySystem`, `DiplomacySystem`), AI, Queries, Registries, UI.

Worth a second pass later — `EventBus` and `CommandProcessor` in particular are multiplayer-critical
and only need a lightweight fixture rather than real data files.
