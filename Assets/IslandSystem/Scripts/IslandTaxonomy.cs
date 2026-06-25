using System.Collections.Generic;

namespace IslandSystem
{
    /// <summary>
    /// Reference data tying <see cref="IslandType"/>s to their <see cref="ClimateZone"/> and to a
    /// human-readable (Russian) display name. This is the authoritative grouping — keep it in sync if the
    /// <see cref="IslandType"/> enum changes.
    /// </summary>
    public static class IslandTaxonomy
    {
        /// <summary>Which climate zone each island type belongs to.</summary>
        public static readonly IReadOnlyDictionary<IslandType, ClimateZone> Climate =
            new Dictionary<IslandType, ClimateZone>
            {
                // Cold
                { IslandType.PolarArchipelago, ClimateZone.Cold },
                { IslandType.PolarCanyons,     ClimateZone.Cold },
                { IslandType.GlacialCoast,     ClimateZone.Cold },
                { IslandType.BorealTaiga,      ClimateZone.Cold },
                { IslandType.Tundra,           ClimateZone.Cold },
                { IslandType.GlacierHighlands, ClimateZone.Cold },

                // Tropical
                { IslandType.ParadiseIslands,    ClimateZone.Tropical },
                { IslandType.MangroveCoast,      ClimateZone.Tropical },
                { IslandType.TropicalHighlands,  ClimateZone.Tropical },
                { IslandType.BambooIsles,        ClimateZone.Tropical },
                { IslandType.RockyTropicalCoast, ClimateZone.Tropical },
                { IslandType.RainforestIslands,  ClimateZone.Tropical },

                // Hot
                { IslandType.ClassicDesert,   ClimateZone.Hot },
                { IslandType.GrandCanyons,    ClimateZone.Hot },
                { IslandType.WildWest,        ClimateZone.Hot },
                { IslandType.RedRockBadlands, ClimateZone.Hot },
                { IslandType.LavaWastelands,  ClimateZone.Hot },
                { IslandType.RockPlateau,     ClimateZone.Hot },

                // Temperate
                { IslandType.EuropeanCountryside,   ClimateZone.Temperate },
                { IslandType.SlavicWilderness,      ClimateZone.Temperate },
                { IslandType.LakeDistrict,          ClimateZone.Temperate },
                { IslandType.RollingHighlands,      ClimateZone.Temperate },
                { IslandType.MountainValleys,       ClimateZone.Temperate },
                { IslandType.ConiferousArchipelago, ClimateZone.Temperate },
                { IslandType.MixedWoodlands,        ClimateZone.Temperate },
            };

        /// <summary>Russian display name for each island type (for UI / tooling).</summary>
        public static readonly IReadOnlyDictionary<IslandType, string> DisplayNameRu =
            new Dictionary<IslandType, string>
            {
                { IslandType.PolarArchipelago, "Полярный архипелаг" },
                { IslandType.PolarCanyons,     "Ледяные каньоны" },
                { IslandType.GlacialCoast,     "Ледниковое побережье" },
                { IslandType.BorealTaiga,      "Хвойная тайга" },
                { IslandType.Tundra,           "Тундровые острова" },
                { IslandType.GlacierHighlands, "Горные ледники" },

                { IslandType.ParadiseIslands,    "Райские острова" },
                { IslandType.MangroveCoast,      "Мангровое побережье" },
                { IslandType.TropicalHighlands,  "Тропические нагорья" },
                { IslandType.BambooIsles,        "Бамбуковые острова" },
                { IslandType.RockyTropicalCoast, "Скалистые тропические берега" },
                { IslandType.RainforestIslands,  "Дождевые леса" },

                { IslandType.ClassicDesert,   "Классическая пустыня" },
                { IslandType.GrandCanyons,    "Каньоны" },
                { IslandType.WildWest,        "Дикий Запад" },
                { IslandType.RedRockBadlands, "Красные скалы" },
                { IslandType.LavaWastelands,  "Лавовые пустоши" },
                { IslandType.RockPlateau,     "Каменистое плато" },

                { IslandType.EuropeanCountryside,   "Европейская сельская местность" },
                { IslandType.SlavicWilderness,      "Славянская глубинка" },
                { IslandType.LakeDistrict,          "Озёрный край" },
                { IslandType.RollingHighlands,      "Холмистые острова" },
                { IslandType.MountainValleys,       "Горные долины" },
                { IslandType.ConiferousArchipelago, "Хвойный архипелаг" },
                { IslandType.MixedWoodlands,        "Смешанные леса" },
            };

        /// <summary>The climate zone an island type belongs to.</summary>
        public static ClimateZone ClimateOf(IslandType type) => Climate[type];

        /// <summary>Russian label for an island type, falling back to the enum name.</summary>
        public static string DisplayName(IslandType type)
            => DisplayNameRu.TryGetValue(type, out var n) ? n : type.ToString();

        /// <summary>All island types that belong to the given climate zone.</summary>
        public static List<IslandType> TypesIn(ClimateZone zone)
        {
            var list = new List<IslandType>();
            foreach (var kv in Climate)
                if (kv.Value == zone) list.Add(kv.Key);
            return list;
        }
    }
}
