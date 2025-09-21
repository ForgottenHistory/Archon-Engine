using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct StringInternSystem : IDisposable
    {
        private NativeHashMap<uint, int> m_HashToStringId;
        private NativeStringPool m_StringPool;
        private readonly Allocator m_Allocator;

        public bool IsCreated => m_HashToStringId.IsCreated && m_StringPool.IsCreated;

        public StringInternSystem(int initialCapacity, Allocator allocator)
        {
            m_HashToStringId = new NativeHashMap<uint, int>(initialCapacity, allocator);
            m_StringPool = new NativeStringPool(initialCapacity, allocator);
            m_Allocator = allocator;
        }

        public int InternString(in FixedString128Bytes str)
        {
            var hash = CalculateHash(str);

            if (m_HashToStringId.TryGetValue(hash, out int existingId))
            {
                return existingId;
            }

            var stringId = m_StringPool.InternString(str);
            m_HashToStringId[hash] = stringId;
            return stringId;
        }

        public int InternString(string str)
        {
            var fixedStr = new FixedString128Bytes(str);
            return InternString(in fixedStr);
        }

        public FixedString128Bytes GetString(int stringId)
        {
            return m_StringPool.GetString(stringId);
        }

        public bool TryGetString(int stringId, out FixedString128Bytes result)
        {
            return m_StringPool.TryGetString(stringId, out result);
        }

        public bool Contains(in FixedString128Bytes str)
        {
            var hash = CalculateHash(str);
            return m_HashToStringId.ContainsKey(hash);
        }

        public bool TryGetStringId(in FixedString128Bytes str, out int stringId)
        {
            var hash = CalculateHash(str);
            return m_HashToStringId.TryGetValue(hash, out stringId);
        }

        public int Count => m_StringPool.Count;

        private static uint CalculateHash(in FixedString128Bytes str)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < str.Length; i++)
            {
                hash = (hash ^ str[i]) * 16777619u;
            }
            return hash;
        }

        public void Dispose()
        {
            if (m_HashToStringId.IsCreated)
                m_HashToStringId.Dispose();
            if (m_StringPool.IsCreated)
                m_StringPool.Dispose();
        }
    }

    public static class CommonStrings
    {
        public static readonly FixedString32Bytes Empty = "";
        public static readonly FixedString32Bytes True = "yes";
        public static readonly FixedString32Bytes False = "no";
        public static readonly FixedString32Bytes Name = "name";
        public static readonly FixedString32Bytes Tag = "tag";
        public static readonly FixedString32Bytes Id = "id";
        public static readonly FixedString32Bytes Color = "color";
        public static readonly FixedString32Bytes Capital = "capital";
        public static readonly FixedString32Bytes Culture = "culture";
        public static readonly FixedString32Bytes Religion = "religion";
        public static readonly FixedString32Bytes TechGroup = "technology_group";
        public static readonly FixedString32Bytes Government = "government";

        public static void PreIntern(ref StringInternSystem internSystem)
        {
            internSystem.InternString(Empty);
            internSystem.InternString(True);
            internSystem.InternString(False);
            internSystem.InternString(Name);
            internSystem.InternString(Tag);
            internSystem.InternString(Id);
            internSystem.InternString(Color);
            internSystem.InternString(Capital);
            internSystem.InternString(Culture);
            internSystem.InternString(Religion);
            internSystem.InternString(TechGroup);
            internSystem.InternString(Government);
        }
    }
}