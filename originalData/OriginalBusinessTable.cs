using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Company and business catalogue from Capone.EXE: 161 records × 48 bytes at 0x47DDC0 (read by the company class,
/// vtable off_4803CC; an identical copy at 0x4737C0 is read by Building::GetCatalogueIncome, sub_402782).
/// Rows 0–41 are legal companies, 42–159 businesses, 160 "Объект Х".
/// </summary>
public class OriginalBusinessTable
{
    public const uint TableAddress = 0x47DDC0;
    public const int RecordSize = 0x30;
    public const int RecordCount = 161;

    /// <summary>Record +0x10.</summary>
    public enum BusinessType
    {
        Legal = 0,
        Bootlegging = 1,
        Entertainment = 2,
        Gambling = 3,
        Troubleshooting = 4,
        /// <summary>Fortification, cash-out, training: negative income (upkeep).</summary>
        Support = 5,
    }

    private static OriginalBusinessTable _instance;
    public static OriginalBusinessTable Instance => _instance ??= new OriginalBusinessTable();

    public List<Entry> Entries { get; } = new();

    private OriginalBusinessTable()
    {
        var exe = OriginalExe.Instance;
        if (!exe.IsLoaded)
            return;
        for (var i = 0; i < RecordCount; i++)
        {
            var va = TableAddress + (uint)(i * RecordSize);
            var namePtr = exe.ReadU32(va);
            if (!exe.IsValidAddress(namePtr))
            {
                GD.PrintErr($"OriginalBusinessTable: bad record {i} at 0x{va:X}");
                break;
            }
            Entries.Add(new Entry
            {
                Index = i,
                Name = exe.ReadCString(namePtr),
                Price = exe.ReadI32(va + 0x04),
                Income = exe.ReadI32(va + 0x08),
                Grade = exe.ReadI32(va + 0x0C),
                Type = (BusinessType)exe.ReadI32(va + 0x10),
                MaxHP = exe.ReadI32(va + 0x14),
                Value18 = exe.ReadI32(va + 0x18),
                Value1C = exe.ReadI32(va + 0x1C),
                Value20 = exe.ReadI32(va + 0x20),
                Value24 = exe.ReadI32(va + 0x24),
                Value28 = exe.ReadI32(va + 0x28),
                Flag2C = exe.ReadI32(va + 0x2C),
            });
        }
    }

    public Entry Get(int index) => index >= 0 && index < Entries.Count ? Entries[index] : null;

    /// <summary>Legal companies of a grade (1 slum, 2 middle class, 3 center).</summary>
    public IEnumerable<Entry> LegalOfGrade(int grade) =>
        Entries.Where(e => e.Type == BusinessType.Legal && e.Grade == grade);

    /// <summary>+24h value of rows that may stand in any district.</summary>
    public const int AnyDistrict = 3;

    /// <summary>
    /// fnSelectBuildingRandomly (0x4202A0): a random legal row that may stand in this district (+24h = district or 3)
    /// with this grade (+0Ch = grade or 0). The original draws rows until one fits, which is the same as a uniform pick.
    /// </summary>
    public Entry RandomLegal(int grade, int district)
    {
        var fits = Entries.Where(e => e.Type == BusinessType.Legal
                                      && (e.Value24 == AnyDistrict || e.Value24 == district)
                                      && (e.Grade == 0 || e.Grade == grade)).ToList();
        return fits.Count == 0 ? null : fits[GD.RandRange(0, fits.Count - 1)];
    }

    public class Entry
    {
        public int Index;
        public string Name = "";
        /// <summary>+0x04 price of setting the business up (0 for legal companies).</summary>
        public int Price;
        /// <summary>+0x08 income per period; negative = upkeep (support businesses).</summary>
        public int Income;
        /// <summary>+0x0C 1 slum, 2 middle class, 3 center, 4 top.</summary>
        public int Grade;
        /// <summary>+0x10.</summary>
        public BusinessType Type;
        /// <summary>+0x14 hit points (500 / 800 / 1500).</summary>
        public int MaxHP;
        /// <summary>+0x18 4 / 200 / 400 (police favour needed? not verified).</summary>
        public int Value18;
        /// <summary>+0x1C ≈ price / 250 (not verified).</summary>
        public int Value1C;
        /// <summary>+0x20 1 or 2.</summary>
        public int Value20;
        /// <summary>+0x24 district the row may be placed in: 0 slum, 1 middle class, 2 center, 3 any (fnSelectBuildingRandomly).</summary>
        public int Value24;
        /// <summary>+0x28 2/3/4/7; 3 = cash-out business where collectors can deliver (company vt+1A8h, sub_429221).</summary>
        public int Value28;
        /// <summary>+0x2C 0/1.</summary>
        public int Flag2C;

        public override string ToString() => $"{Index} {Name} ({Type}, grade {Grade}): price {Price}, income {Income}, HP {MaxHP}";
    }
}
