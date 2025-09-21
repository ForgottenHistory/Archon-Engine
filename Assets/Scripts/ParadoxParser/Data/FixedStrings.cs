using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    public unsafe struct FixedString4 : IEquatable<FixedString4>
    {
        private fixed byte m_bytes[4];

        public int Length { get; private set; }

        public FixedString4(string str)
        {
            Length = 0;
            Clear();
            if (str != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(str);
                var copyLength = math.min(bytes.Length, 4);
                for (int i = 0; i < copyLength; i++)
                {
                    m_bytes[i] = bytes[i];
                }
                Length = copyLength;
            }
        }

        private void Clear()
        {
            for (int i = 0; i < 4; i++)
            {
                m_bytes[i] = 0;
            }
        }

        public override string ToString()
        {
            if (Length == 0) return string.Empty;

            var bytes = new byte[Length];
            for (int i = 0; i < Length; i++)
            {
                bytes[i] = m_bytes[i];
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public bool Equals(FixedString4 other)
        {
            if (Length != other.Length) return false;
            for (int i = 0; i < Length; i++)
            {
                if (m_bytes[i] != other.m_bytes[i]) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is FixedString4 other && Equals(other);
        public override int GetHashCode()
        {
            uint hash = 2166136261u;
            for (int i = 0; i < Length; i++)
            {
                hash = (hash ^ m_bytes[i]) * 16777619u;
            }
            return (int)hash;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    public unsafe struct FixedString8 : IEquatable<FixedString8>
    {
        private fixed byte m_bytes[8];

        public int Length { get; private set; }

        public FixedString8(string str)
        {
            Length = 0;
            Clear();
            if (str != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(str);
                var copyLength = math.min(bytes.Length, 8);
                for (int i = 0; i < copyLength; i++)
                {
                    m_bytes[i] = bytes[i];
                }
                Length = copyLength;
            }
        }

        private void Clear()
        {
            for (int i = 0; i < 8; i++)
            {
                m_bytes[i] = 0;
            }
        }

        public override string ToString()
        {
            if (Length == 0) return string.Empty;

            var bytes = new byte[Length];
            for (int i = 0; i < Length; i++)
            {
                bytes[i] = m_bytes[i];
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public bool Equals(FixedString8 other)
        {
            if (Length != other.Length) return false;
            for (int i = 0; i < Length; i++)
            {
                if (m_bytes[i] != other.m_bytes[i]) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is FixedString8 other && Equals(other);
        public override int GetHashCode()
        {
            uint hash = 2166136261u;
            for (int i = 0; i < Length; i++)
            {
                hash = (hash ^ m_bytes[i]) * 16777619u;
            }
            return (int)hash;
        }
    }

    public static class FixedStringUtils
    {
        public static FixedString4 ToTag(string str)
        {
            return new FixedString4(str?.Substring(0, math.min(str.Length, 3)));
        }

        public static FixedString8 ToShortName(string str)
        {
            return new FixedString8(str?.Substring(0, math.min(str.Length, 7)));
        }
    }
}