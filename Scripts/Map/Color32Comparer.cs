using UnityEngine;
using System.Collections.Generic;

namespace ProvinceSystem
{
    /// <summary>
    /// Custom equality comparer for Color32 to ensure proper dictionary/hashset behavior
    /// </summary>
    public class Color32Comparer : IEqualityComparer<Color32>
    {
        public bool Equals(Color32 x, Color32 y)
        {
            return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
        }

        public int GetHashCode(Color32 obj)
        {
            // Combine RGBA values into a single hash
            return ((int)obj.r << 24) | ((int)obj.g << 16) | ((int)obj.b << 8) | (int)obj.a;
        }
    }
}