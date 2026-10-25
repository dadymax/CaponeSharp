using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Per-type unit stats from Capone.EXE: 13 records of 52 bytes at 0x484030, indexed by the unit type number
/// (the same number the .epi UNIT line uses). Field meanings were derived from the getters around 0x42A150–0x42E4F3
/// and checked against costs in orig_unit_handheld_data.txt.
/// </summary>
public class OriginalUnitTable
{
    public const uint TableAddress = 0x484030;
    public const int RecordSize = 52;
    public const int RecordCount = 13;

    private static OriginalUnitTable _instance;
    public static OriginalUnitTable Instance => _instance ??= new OriginalUnitTable();

    private readonly Dictionary<int, Stats> _stats = new();

    /// <summary>Address of each type's EN/ES/RU name triple (string pointer table at 0x494EEC and 0x4952F4).</summary>
    private static readonly Dictionary<int, uint> NameTriples = new()
    {
        [0] = 0x494EEC,  // Thug / Вышибала
        [1] = 0x494EF8,  // Pistol man / Стрелок
        [2] = 0x494F04,  // Thompson man / Гробовщик
        [3] = 0x494F10,  // Terrorist / Террорист
        [4] = 0x494F1C,  // Sniper / Снайпер
        [5] = 0x494F34,  // Cadillac / Кадиллак
        [7] = 0x494F28,  // Collector / Курьер
        [8] = 0x494F40,  // Shotgun man / Налётчик
        [9] = 0x494F4C,  // Hit man / Киллер
        [10] = 0x494F58, // Thief / Вор
        [11] = 0x494F7C, // Sergeant / Сержант
        [12] = 0x4952F4, // Godfather / Дон
    };

    private OriginalUnitTable()
    {
        var exe = OriginalExe.Instance;
        if (!exe.IsLoaded)
            return;
        for (var type = 0; type < RecordCount; type++)
        {
            var va = TableAddress + (uint)(type * RecordSize);
            _stats[type] = new Stats
            {
                Type = type,
                Unknown00 = exe.ReadF32(va + 0x00),
                DamageMin = exe.ReadU16(va + 0x04),
                DamageMax = exe.ReadU16(va + 0x06),
                BuildingDamageMin = exe.ReadU16(va + 0x08),
                BuildingDamageMax = exe.ReadU16(va + 0x0A),
                HitChance = exe.ReadU16(va + 0x0C),
                MaxHP = exe.ReadU16(va + 0x0E),
                AttackRange = exe.ReadF32(va + 0x14),
                TicksPerTile = exe.ReadU16(va + 0x18),
                Value1C = exe.ReadI32(va + 0x1C),
                Cost = exe.ReadI32(va + 0x20),
                Value24 = exe.ReadU16(va + 0x24),
                AttackKind = exe.ReadU16(va + 0x26),
                Value28 = exe.ReadU16(va + 0x28),
                VisionMax = exe.ReadU16(va + 0x2A),
                Vision = exe.ReadU16(va + 0x2C),
                Value2E = exe.ReadU16(va + 0x2E),
                Value30 = exe.ReadU16(va + 0x30),
            };
        }
    }

    public Stats Get(int type) => _stats.GetValueOrDefault(type);

    /// <summary>Unit name in the given language, or "" if the EXE has none for this type.</summary>
    public static string GetName(int type, OriginalExe.Language language)
    {
        var exe = OriginalExe.Instance;
        return exe.IsLoaded && NameTriples.TryGetValue(type, out var va) ? exe.ReadTriple(va, language) : "";
    }

    public class Stats
    {
        public int Type;
        /// <summary>+0x00, float. 1.0 for most; Thompson 6, Sniper 0.24, police car 4. Compared with 1.0 in sub_42BBB0.</summary>
        public float Unknown00;
        /// <summary>+0x04/+0x06: damage range against units.</summary>
        public int DamageMin, DamageMax;
        /// <summary>+0x08/+0x0A: damage range against buildings.</summary>
        public int BuildingDamageMin, BuildingDamageMax;
        /// <summary>+0x0C: chance to hit, percent (Terrorist 100).</summary>
        public int HitChance;
        /// <summary>+0x0E: hit points.</summary>
        public int MaxHP;
        /// <summary>+0x14: attack range in tiles.</summary>
        public float AttackRange;
        /// <summary>+0x18: game ticks per tile walked (60; Cadillac 30, police car 20).</summary>
        public int TicksPerTile;
        /// <summary>+0x1C: second money value (equals Cost except Collector 15000 vs 5000, police car 400000 vs 375000).</summary>
        public int Value1C;
        /// <summary>+0x20: price; copied into the unit at creation.</summary>
        public int Cost;
        /// <summary>+0x24: 600 Thug, 1050 Pistol, 1200 others. Probably training time.</summary>
        public int Value24;
        /// <summary>+0x26: 1 normally, 2 for Terrorist and Shotgun man (area attackers).</summary>
        public int AttackKind;
        /// <summary>+0x28: 50 for Thompson man only.</summary>
        public int Value28;
        /// <summary>+0x2A: largest vision radius (Sniper 9 when standing still).</summary>
        public int VisionMax;
        /// <summary>+0x2C: normal vision radius in tiles.</summary>
        public int Vision;
        /// <summary>+0x2E: 300 for Shotgun man only.</summary>
        public int Value2E;
        /// <summary>+0x30: 1 for Thompson man only.</summary>
        public int Value30;
    }
}
