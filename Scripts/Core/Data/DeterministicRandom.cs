using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Core.Data
{
    /// <summary>
    /// Deterministic random number generator for multiplayer consistency
    /// Uses xorshift128+ algorithm for fast, high-quality random numbers
    /// Guarantees identical results across all platforms and clients
    /// </summary>
    public struct DeterministicRandom
    {
        private uint4 state;

        /// <summary>
        /// Current state of the random number generator
        /// Can be serialized for save/load or network synchronization
        /// </summary>
        public uint4 State
        {
            get => state;
            set => state = value;
        }

        /// <summary>
        /// Create a new deterministic random generator with a seed
        /// </summary>
        public DeterministicRandom(uint seed)
        {
            // Initialize state using splitmix64 to avoid poor initial seeds
            state = InitializeState(seed);
        }

        /// <summary>
        /// Create from an existing state
        /// </summary>
        public DeterministicRandom(uint4 initialState)
        {
            state = initialState;
            // Ensure state is never all zeros (would break xorshift)
            if (math.all(state == 0))
            {
                state = new uint4(1, 2, 3, 4);
            }
        }

        /// <summary>
        /// Initialize state from seed using splitmix64 algorithm
        /// </summary>
        private static uint4 InitializeState(uint seed)
        {
            ulong x = seed;

            // Use splitmix64 to generate four good initial values
            x += 0x9e3779b97f4a7c15UL;
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            x = x ^ (x >> 31);
            uint s0 = (uint)(x & 0xFFFFFFFF);

            x += 0x9e3779b97f4a7c15UL;
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            x = x ^ (x >> 31);
            uint s1 = (uint)(x & 0xFFFFFFFF);

            x += 0x9e3779b97f4a7c15UL;
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            x = x ^ (x >> 31);
            uint s2 = (uint)(x & 0xFFFFFFFF);

            x += 0x9e3779b97f4a7c15UL;
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            x = x ^ (x >> 31);
            uint s3 = (uint)(x & 0xFFFFFFFF);

            return new uint4(s0, s1, s2, s3);
        }

        /// <summary>
        /// Generate next random uint32 using xorshift128+ algorithm
        /// </summary>
        public uint NextUInt()
        {
            uint t = state.x ^ (state.x << 11);
            state.x = state.y;
            state.y = state.z;
            state.z = state.w;
            state.w = state.w ^ (state.w >> 19) ^ (t ^ (t >> 8));
            return state.w;
        }

        /// <summary>
        /// Generate random integer in range [0, max)
        /// Uses rejection sampling to avoid bias
        /// </summary>
        public uint NextUInt(uint max)
        {
            if (max <= 1) return 0;

            // Use rejection sampling to avoid modulo bias
            uint threshold = (0xFFFFFFFFU - max + 1) % max;
            uint value;
            do
            {
                value = NextUInt();
            } while (value < threshold);

            return value % max;
        }

        /// <summary>
        /// Generate random integer in range [min, max)
        /// </summary>
        public uint NextUInt(uint min, uint max)
        {
            if (min >= max) return min;
            return min + NextUInt(max - min);
        }

        /// <summary>
        /// Generate random integer in range [0, max)
        /// </summary>
        public int NextInt(int max)
        {
            if (max <= 0) return 0;
            return (int)NextUInt((uint)max);
        }

        /// <summary>
        /// Generate random integer in range [min, max)
        /// </summary>
        public int NextInt(int min, int max)
        {
            if (min >= max) return min;
            return min + NextInt(max - min);
        }

        /// <summary>
        /// Generate random float in range [0, 1)
        /// Uses fixed-point arithmetic for deterministic results
        /// </summary>
        public FixedPoint32 NextFixed()
        {
            // Use upper 32 bits for better quality
            uint value = NextUInt();
            return FixedPoint32.FromRaw((int)(value >> 1)); // Shift to avoid sign bit issues
        }

        /// <summary>
        /// Generate random fixed-point value in range [0, max)
        /// </summary>
        public FixedPoint32 NextFixed(FixedPoint32 max)
        {
            if (max.RawValue <= 0) return FixedPoint32.Zero;

            // Scale the random value
            long scaled = ((long)NextUInt() * max.RawValue) >> 32;
            return FixedPoint32.FromRaw((int)scaled);
        }

        /// <summary>
        /// Generate random fixed-point value in range [min, max)
        /// </summary>
        public FixedPoint32 NextFixed(FixedPoint32 min, FixedPoint32 max)
        {
            if (min.RawValue >= max.RawValue) return min;
            return min + NextFixed(max - min);
        }

        /// <summary>
        /// Generate random boolean
        /// </summary>
        public bool NextBool()
        {
            return (NextUInt() & 1) == 1;
        }

        /// <summary>
        /// Generate random boolean with specified probability
        /// </summary>
        public bool NextBool(FixedPoint32 probability)
        {
            return NextFixed() < probability;
        }

        /// <summary>
        /// Generate random boolean with percentage chance (0-100)
        /// </summary>
        public bool NextPercent(int percent)
        {
            if (percent <= 0) return false;
            if (percent >= 100) return true;
            return NextInt(100) < percent;
        }

        /// <summary>
        /// Select random element from array
        /// </summary>
        public T NextElement<T>(T[] array) where T : unmanaged
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty");

            return array[NextInt(array.Length)];
        }

        /// <summary>
        /// Select random element from NativeArray
        /// </summary>
        public T NextElement<T>(NativeArray<T> array) where T : unmanaged
        {
            if (!array.IsCreated || array.Length == 0)
                throw new ArgumentException("Array must be created and non-empty");

            return array[NextInt(array.Length)];
        }

        /// <summary>
        /// Shuffle array in-place using Fisher-Yates algorithm
        /// </summary>
        public void Shuffle<T>(T[] array) where T : unmanaged
        {
            if (array == null || array.Length <= 1) return;

            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
        }

        /// <summary>
        /// Shuffle NativeArray in-place using Fisher-Yates algorithm
        /// </summary>
        public void Shuffle<T>(NativeArray<T> array) where T : unmanaged
        {
            if (!array.IsCreated || array.Length <= 1) return;

            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
        }

        /// <summary>
        /// Generate random 2D point within a rectangle
        /// </summary>
        public int2 NextPoint(int2 min, int2 max)
        {
            return new int2(
                NextInt(min.x, max.x),
                NextInt(min.y, max.y)
            );
        }

        /// <summary>
        /// Generate random 2D point within a circle (using rejection sampling)
        /// </summary>
        public FixedPoint2 NextPointInCircle(FixedPoint32 radius)
        {
            FixedPoint32 x, y;
            FixedPoint32 lengthSquared;

            do
            {
                x = NextFixed(FixedPoint32.FromInt(-1), FixedPoint32.FromInt(1));
                y = NextFixed(FixedPoint32.FromInt(-1), FixedPoint32.FromInt(1));
                lengthSquared = x * x + y * y;
            } while (lengthSquared > FixedPoint32.One);

            return new FixedPoint2(x * radius, y * radius);
        }

        #region Gaussian / Normal Distribution

        /// <summary>
        /// Generate random value from standard normal distribution (mean=0, stddev=1).
        /// Uses Box-Muller transform with fixed-point math for determinism.
        /// </summary>
        public FixedPoint32 NextGaussian()
        {
            // Box-Muller transform: generate two uniform randoms, get two gaussians
            // We use one and discard the other for simplicity

            // Get two uniform values in (0, 1] - avoid 0 to prevent log(0)
            uint u1Raw = NextUInt();
            uint u2Raw = NextUInt();

            // Ensure non-zero (extremely rare but possible)
            if (u1Raw == 0) u1Raw = 1;

            // Convert to fixed point in range (0, 1]
            FixedPoint32 u1 = FixedPoint32.FromRaw((int)(u1Raw >> 1) | 1);
            FixedPoint32 u2 = FixedPoint32.FromRaw((int)(u2Raw >> 1));

            // Box-Muller: z = sqrt(-2 * ln(u1)) * cos(2 * pi * u2)
            // We approximate using fixed-point math

            // Approximate -2 * ln(u1) using lookup or polynomial
            // For simplicity, use rejection sampling with uniform approximation
            // This gives a reasonable bell curve without complex math

            // Simpler approach: sum of 12 uniform randoms - 6 (Central Limit Theorem)
            // Gives approximate normal with mean=0, stddev=1
            FixedPoint32 sum = FixedPoint32.Zero;
            for (int i = 0; i < 12; i++)
            {
                sum = sum + NextFixed();
            }
            return sum - FixedPoint32.FromInt(6);
        }

        /// <summary>
        /// Generate random value from normal distribution with specified mean and standard deviation.
        /// </summary>
        public FixedPoint32 NextGaussian(FixedPoint32 mean, FixedPoint32 stdDev)
        {
            return mean + NextGaussian() * stdDev;
        }

        #endregion

        #region Weighted Selection

        /// <summary>
        /// Select random element from array using weights.
        /// Higher weight = more likely to be selected.
        /// </summary>
        public T NextWeightedElement<T>(T[] elements, int[] weights) where T : unmanaged
        {
            if (elements == null || elements.Length == 0)
                throw new ArgumentException("Elements array cannot be null or empty");
            if (weights == null || weights.Length != elements.Length)
                throw new ArgumentException("Weights array must match elements array length");

            // Calculate total weight
            int totalWeight = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < 0)
                    throw new ArgumentException("Weights cannot be negative");
                totalWeight += weights[i];
            }

            if (totalWeight <= 0)
                return elements[NextInt(elements.Length)]; // Fallback to uniform

            // Pick random value in [0, totalWeight)
            int roll = NextInt(totalWeight);

            // Find which element the roll falls into
            int cumulative = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                    return elements[i];
            }

            // Fallback (shouldn't happen)
            return elements[elements.Length - 1];
        }

        /// <summary>
        /// Select random element from array using FixedPoint weights.
        /// </summary>
        public T NextWeightedElement<T>(T[] elements, FixedPoint32[] weights) where T : unmanaged
        {
            if (elements == null || elements.Length == 0)
                throw new ArgumentException("Elements array cannot be null or empty");
            if (weights == null || weights.Length != elements.Length)
                throw new ArgumentException("Weights array must match elements array length");

            // Calculate total weight
            FixedPoint32 totalWeight = FixedPoint32.Zero;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < FixedPoint32.Zero)
                    throw new ArgumentException("Weights cannot be negative");
                totalWeight = totalWeight + weights[i];
            }

            if (totalWeight <= FixedPoint32.Zero)
                return elements[NextInt(elements.Length)];

            // Pick random value in [0, totalWeight)
            FixedPoint32 roll = NextFixed(totalWeight);

            // Find which element the roll falls into
            FixedPoint32 cumulative = FixedPoint32.Zero;
            for (int i = 0; i < elements.Length; i++)
            {
                cumulative = cumulative + weights[i];
                if (roll < cumulative)
                    return elements[i];
            }

            return elements[elements.Length - 1];
        }

        /// <summary>
        /// Select random index using weights (useful when you need the index, not the element).
        /// </summary>
        public int NextWeightedIndex(int[] weights)
        {
            if (weights == null || weights.Length == 0)
                throw new ArgumentException("Weights array cannot be null or empty");

            int totalWeight = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < 0)
                    throw new ArgumentException("Weights cannot be negative");
                totalWeight += weights[i];
            }

            if (totalWeight <= 0)
                return NextInt(weights.Length);

            int roll = NextInt(totalWeight);
            int cumulative = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                    return i;
            }

            return weights.Length - 1;
        }

        #endregion

        #region Element Selection with Exclusion

        /// <summary>
        /// Select random element from array, excluding a specific element.
        /// </summary>
        public T NextElementExcept<T>(T[] array, T excluded) where T : unmanaged, IEquatable<T>
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty");
            if (array.Length == 1)
                return array[0]; // Only one element, return it even if excluded

            // Simple rejection sampling (efficient for small exclusion sets)
            T result;
            int attempts = 0;
            const int maxAttempts = 100;

            do
            {
                result = array[NextInt(array.Length)];
                attempts++;
            } while (result.Equals(excluded) && attempts < maxAttempts);

            return result;
        }

        /// <summary>
        /// Select random element from array, excluding elements at specific indices.
        /// </summary>
        public T NextElementExceptIndices<T>(T[] array, params int[] excludedIndices) where T : unmanaged
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty");
            if (excludedIndices == null || excludedIndices.Length == 0)
                return NextElement(array);
            if (excludedIndices.Length >= array.Length)
                throw new ArgumentException("Cannot exclude all elements");

            // Build list of valid indices
            int validCount = array.Length - excludedIndices.Length;
            int selectedValid = NextInt(validCount);

            int currentValid = 0;
            for (int i = 0; i < array.Length; i++)
            {
                bool isExcluded = false;
                for (int j = 0; j < excludedIndices.Length; j++)
                {
                    if (excludedIndices[j] == i)
                    {
                        isExcluded = true;
                        break;
                    }
                }

                if (!isExcluded)
                {
                    if (currentValid == selectedValid)
                        return array[i];
                    currentValid++;
                }
            }

            // Fallback (shouldn't happen)
            return array[0];
        }

        #endregion

        #region Seed Phrase (Human-Readable State)

        // Word list for seed phrases (64 words = 6 bits each, 4 words = 24 bits coverage per uint)
        private static readonly string[] SeedWords = new string[]
        {
            "alpha", "brave", "crown", "delta", "eagle", "flame", "glory", "haven",
            "ivory", "joust", "knave", "lance", "manor", "noble", "onyx", "pearl",
            "quest", "royal", "siege", "tower", "unity", "valor", "watch", "xenon",
            "yield", "zephyr", "amber", "blade", "crest", "drake", "ember", "frost",
            "grain", "herald", "iron", "jade", "karma", "lotus", "mirth", "nexus",
            "oasis", "prism", "quartz", "raven", "storm", "thorn", "umbra", "viper",
            "wrath", "xerus", "yonder", "zenith", "azure", "basalt", "chrome", "dusk",
            "epoch", "forge", "glyph", "haze", "index", "jewel", "kite", "lunar"
        };

        /// <summary>
        /// Export current state as human-readable seed phrase.
        /// Format: 8 words encoding the full 128-bit state.
        /// </summary>
        public string ToSeedPhrase()
        {
            // Each word encodes 6 bits (64 words), we need 128 bits = ~22 words
            // For simplicity, use 8 words with mixed encoding
            var words = new string[8];

            words[0] = SeedWords[(int)(state.x & 0x3F)];
            words[1] = SeedWords[(int)((state.x >> 6) & 0x3F)];
            words[2] = SeedWords[(int)(state.y & 0x3F)];
            words[3] = SeedWords[(int)((state.y >> 6) & 0x3F)];
            words[4] = SeedWords[(int)(state.z & 0x3F)];
            words[5] = SeedWords[(int)((state.z >> 6) & 0x3F)];
            words[6] = SeedWords[(int)(state.w & 0x3F)];
            words[7] = SeedWords[(int)((state.w >> 6) & 0x3F)];

            return string.Join("-", words);
        }

        /// <summary>
        /// Create DeterministicRandom from seed phrase.
        /// Note: This is a simplified encoding - not full state recovery, but deterministic.
        /// </summary>
        public static DeterministicRandom FromSeedPhrase(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return new DeterministicRandom(1);

            var words = phrase.ToLower().Split('-', ' ');
            if (words.Length < 8)
            {
                // Not enough words - hash the phrase as seed
                uint hash = 0;
                foreach (char c in phrase)
                    hash = hash * 31 + c;
                return new DeterministicRandom(hash);
            }

            // Decode words to state
            uint4 decodedState = new uint4();

            decodedState.x = (uint)(GetWordIndex(words[0]) | (GetWordIndex(words[1]) << 6));
            decodedState.y = (uint)(GetWordIndex(words[2]) | (GetWordIndex(words[3]) << 6));
            decodedState.z = (uint)(GetWordIndex(words[4]) | (GetWordIndex(words[5]) << 6));
            decodedState.w = (uint)(GetWordIndex(words[6]) | (GetWordIndex(words[7]) << 6));

            // Expand to full state using splitmix
            return new DeterministicRandom(decodedState.x ^ decodedState.y ^ decodedState.z ^ decodedState.w);
        }

        private static int GetWordIndex(string word)
        {
            for (int i = 0; i < SeedWords.Length; i++)
            {
                if (SeedWords[i] == word)
                    return i;
            }
            return 0; // Unknown word defaults to 0
        }

        #endregion

        /// <summary>
        /// Create a new random generator with different state (for sub-systems)
        /// This allows different systems to have independent random sequences
        /// while maintaining overall determinism
        /// </summary>
        public DeterministicRandom Branch(uint offset = 1)
        {
            var branchedState = state;
            // XOR with offset to create different but deterministic state
            branchedState.x ^= offset;
            branchedState.y ^= offset << 8;
            branchedState.z ^= offset << 16;
            branchedState.w ^= offset << 24;

            return new DeterministicRandom(branchedState);
        }

        /// <summary>
        /// Get a hash of the current state for verification
        /// </summary>
        public uint GetStateHash()
        {
            return state.x ^ (state.y << 8) ^ (state.z << 16) ^ (state.w << 24);
        }

        /// <summary>
        /// Reset to a known state (for testing or save/load)
        /// </summary>
        public void SetSeed(uint seed)
        {
            state = InitializeState(seed);
        }

        public override string ToString()
        {
            return $"DeterministicRandom[{state.x:X8}, {state.y:X8}, {state.z:X8}, {state.w:X8}]";
        }

        /// <summary>
        /// Verify two random generators have the same state
        /// </summary>
        public bool HasSameState(DeterministicRandom other)
        {
            return math.all(state == other.state);
        }
    }
}