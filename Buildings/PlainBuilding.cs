using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// A building cell without a company, selectable like in the original (user playtest 2026-11-04). The EXE puts an
/// object on every block cell (fnBuildingPlacementOnMapByNumber):
/// <list type="bullet">
/// <item>"default building" (fnSetDefaultBuilding, vtable off_475A90, class 1): a B or C cell that got no business
/// (1 in 4) and every D cell. Name vt+18h = text (21, 0); portrait vt+68h (sub_4025F0) by the block's district: slum
/// 0C1Eh, middle class 0C1Dh, center 0C1Bh.</item>
/// <item>"Empty space" (fnSetGroundWalkabilityToNone / sub_420BB0, vtables off_47CDB0 / off_47D3A4, class 3): F and G
/// cells. Empty name (sub_402C70), portrait 0C1Fh (vt+68h sub_420A80).</item>
/// </list>
/// Selecting one plays no sound (generic vt+70h sub_4385D5) and its panel (vt+10h sub_402795) clears the six order
/// buttons and shows the portrait; the info column shows the name and a full HP bar (sub_40A997 draws 100 % when the
/// object has no hit points). One shared node is moved to the clicked cell, since thousands of such cells exist.
/// </summary>
public partial class PlainBuilding : Node2D, ISelectable
{
    /// <summary>Kind codes stored per cell: 0–2 a default building in that district, <see cref="EmptySpace"/> F / G.</summary>
    public const int EmptySpace = 9;

    /// <summary>Map meta: Godot cell → kind code.</summary>
    public static readonly StringName MetaCells = "plainBuildingCells";

    /// <summary>Click radius around a cell's centre, like a company's collision circle (radius 20, 5 px up).</summary>
    private const float PickRadius = 20f;
    private static readonly Vector2 PickOffset = new(0, -5);

    private Selection _selection;

    public Vector2I Cell { get; private set; }
    public int Kind { get; private set; }
    public bool IsEmptySpace => Kind == EmptySpace;
    public bool Selectable => true;

    public bool Selected
    {
        get => _selection?.Enabled ?? false;
        set
        {
            if (_selection != null)
                _selection.Enabled = value;
        }
    }

    /// <summary>vt+18h: text (21, 0) for a default building, "" for empty space.</summary>
    public string DisplayName => IsEmptySpace ? "" : OriginalTexts.Instance.Get(21, 0);

    /// <summary>vt+68h: Chicago.tile portrait index.</summary>
    public string Portrait => Kind switch
    {
        0 => "#3102",
        1 => "#3101",
        2 => "#3099",
        _ => "#3103",
    };

    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://Nodes/Selection.tscn");
        _selection = scene.Instantiate<Selection>();
        _selection.Position = new Vector2(0, -5);
        AddChild(_selection);
    }

    /// <summary>Kind code of a block letter in a block of <paramref name="district"/>, or null for other letters.</summary>
    public static int? KindOf(char letter, int district) => letter switch
    {
        'B' or 'C' or 'D' => Mathf.Clamp(district, 0, 2),
        'F' or 'G' => EmptySpace,
        _ => null,
    };

    /// <summary>
    /// The plain building cell at a map point: the cell whose centre (5 px up) is nearest within the pick radius,
    /// skipping cells that hold a company (a business placed later, an HQ). Null when there is none.
    /// </summary>
    public static (Vector2I Cell, int Kind)? At(TileMapLayer map, Vector2 globalPoint)
    {
        if (map == null || !map.HasMeta(MetaCells))
            return null;
        var cells = map.GetMeta(MetaCells).AsGodotDictionary<Vector2I, int>();
        var local = map.ToLocal(globalPoint);
        var under = map.LocalToMap(local - PickOffset);
        (Vector2I, int)? best = null;
        var bestDistance = PickRadius;
        foreach (var cell in Around(under))
        {
            if (!cells.TryGetValue(cell, out var kind))
                continue;
            if (map.GetCellTileData(cell) is { } td && TileMapGenerator.GetCompany(td) != null)
                continue;
            var d = (map.MapToLocal(cell) + PickOffset).DistanceTo(local);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = (cell, kind);
            }
        }
        return best;
    }

    private static IEnumerable<Vector2I> Around(Vector2I cell)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                yield return cell + new Vector2I(dx, dy);
    }

    /// <summary>Moves the shared node to <paramref name="cell"/>.</summary>
    public void Place(TileMapLayer map, Vector2I cell, int kind)
    {
        Cell = cell;
        Kind = kind;
        Position = map.MapToLocal(cell);
    }
}
