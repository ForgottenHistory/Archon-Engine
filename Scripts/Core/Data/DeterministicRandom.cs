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