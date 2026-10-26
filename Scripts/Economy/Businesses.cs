using System.Collections.Generic;
using System.Linq;

namespace Capone;

/// <summary>
/// Authority services (0x488268, 54 × 12 bytes: word authority, dword price, float points). Authorities: 1 army,
/// 2 FBI, 3 politician, 4 police (the bribe-share texts 5,64–73). A service is available when the money given to the
/// authority reaches the price; using it costs the points (<see cref="Organisation.CanUseItem"/>). Examples: business
/// licences (<see cref="BusinessRules.LicenceItem"/>), lawyers 24h/27h/29h, informers 2Ch/31h/34h.
/// </summary>
public static class AuthorityItems
{
    public const uint TableVa = 0x488268;
    public const int Count = 0x36;

    public class Item
    {
        public int Index;
        public int Authority;
        public long Price;
        public float Points;
    }

    private static List<Item> _items;

    public static Item Get(int index)
    {
        if (_items == null)
        {
            _items = new List<Item>();
            var exe = OriginalExe.Instance;
            if (exe.IsLoaded)
                for (var i = 0; i < Count; i++)
                {
                    var va = TableVa + (uint)(i * 12);
                    _items.Add(new Item { Index = i, Authority = exe.ReadI16(va), Price = exe.ReadI32(va + 4), Points = exe.ReadF32(va + 8) });
                }
        }
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }
}

/// <summary>Business conversion rules of the company menu (sub_421370) and the conversion job (sub_427050).</summary>
public static class BusinessRules
{
    /// <summary>Most rows a type's menu lists (sub_421370 stops at 5; the sixth slot is "Back").</summary>
    public const int MaxOffers = 5;
    /// <summary>Owner's share of the price when a business is sold (dbl_4802B4).</summary>
    public const double SellFactor = 0.5;

    /// <summary>sub_4212C2: price tier 0/1/2 of a row within its district (slum 100 000 / 200 000, middle 750 000 / 1 000 000, center 1 500 000 / 2 000 000).</summary>
    public static int Tier(OriginalBusinessTable.Entry row)
    {
        var (low, high) = row.Value24 switch
        {
            0 => (100_000, 200_000),
            1 => (750_000, 1_000_000),
            2 => (1_500_000, 2_000_000),
            _ => (int.MinValue, int.MinValue),
        };
        return row.Price < low ? 0 : row.Price < high ? 1 : 2;
    }

    private static readonly int[,] Licences =
    {
        { -1, 0x23, 0x28 }, // slum
        { 0x25, 0x2A, 0x30 }, // middle class
        { 0x2E, 0x33, 0x1F }, // center (the dearest needs the politician)
    };

    /// <summary>Licence (authority service) a row needs, by district and tier (sub_431244 / sub_427050); -1 = none.</summary>
    public static int LicenceItem(OriginalBusinessTable.Entry row) =>
        row.Value24 is >= 0 and <= 2 ? Licences[row.Value24, Tier(row)] : -1;

    /// <summary>Player vt+CCh (sub_431244): may the player open this row now?</summary>
    public static bool IsAvailable(Organisation org, OriginalBusinessTable.Entry row)
    {
        var item = LicenceItem(row);
        return item < 0 || org.CanUseItem(item);
    }

    /// <summary>Player vt+D0h (sub_4313C6): favour points the licence costs, for the menu text.</summary>
    public static int FavourNeeded(OriginalBusinessTable.Entry row)
    {
        var item = LicenceItem(row);
        return item < 0 ? 0 : (int)(AuthorityItems.Get(item)?.Points ?? 0);
    }

    /// <summary>
    /// sub_421370 submenu: catalogue rows of <paramref name="type"/> with the company's grade, placeable in its district
    /// (row +24h = 3 or the block's district) and available to the owner; at most <see cref="MaxOffers"/>.
    /// </summary>
    public static List<OriginalBusinessTable.Entry> Offers(Company company, OriginalBusinessTable.BusinessType type)
    {
        var org = company.OwnerTeam?.Org;
        var current = company.Entry;
        if (org == null || current == null)
            return new List<OriginalBusinessTable.Entry>();
        var district = company.Block?.District ?? 3;
        return OriginalBusinessTable.Instance.Entries
            .Where(r => (r.Value24 == 3 || r.Value24 == district) && r.Grade == current.Grade && r.Type == type
                        && IsAvailable(org, r))
            .Take(MaxOffers)
            .ToList();
    }
}
