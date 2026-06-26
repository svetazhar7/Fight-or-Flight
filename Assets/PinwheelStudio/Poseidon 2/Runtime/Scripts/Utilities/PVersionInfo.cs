#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    /// <summary>
    /// Utility class contains product info
    /// </summary>
    public static class PVersionInfo
    {
        public const int Major = 2;
        public const int Minor = 0;
        public const int Patch = 3;

        public static float Number
        {
            get
            {
                return Major * 1.0f + Minor * 1.0f / 100f;
            }
        }

        public static string Code
        {
            get
            {
                return string.Format("{0}.{1}.{2}", Major.ToString(), Minor.ToString(), Patch.ToString());
            }
        }

        public static string ProductName
        {
            get
            {
                return "Poseidon - Low Poly Water";
            }
        }

        public static string ProductNameAndVersion
        {
            get
            {
                return string.Format("{0} v{1}", ProductName, Code);
            }
        }

        public static string ProductNameShort
        {
            get
            {
                return "Poseidon";
            }
        }

        public static string ProductNameAndVersionShort
        {
            get
            {
                return string.Format("{0} v{1}", ProductNameShort, Code);
            }
        }
    }
}

#endif