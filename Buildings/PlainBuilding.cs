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
/// User's rule (playtest 2026-11-04): only default buildings are selectable. F and G cells are lawns, the trees along
/// block edges and park squares (Chicago.tile "CHn …" edge slices, "Park1"–"Park4"); clicking them selects nothing,
/// although the EXE makes them selectable "Empty space" objects (a click on a tall building's lawn used to show the
/// empty-space portrait "info house 1").
/// Selecting one plays no sound (generic vt+70h sub_4385D5) and its panel (vt+10h sub_402795) clears the six order
/// buttons and shows the portrait; the info column shows the name and a full HP bar (sub_40A997 draws 100 % when the
/// object has no hit points). One shared node is moved to the clicked cell, since thousands of such cells exist.
/// </summary>
public partial class PlainBuilding : Node2D, ISelectable
{
    /// <summary>Kind codes stored per cell: 0–2 a default building in that district; <see cref="EmptySpace"/> (the EXE's F / G objects) is not
    /// used, since those cells aren't selectable.</summary>
    public const int EmptySpace = 9;

    /// <summary>Map meta: Godot cell → kind code.</summary>
    public static readonly StringName MetaCells = "plainBuildingCells";

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

    /// <summary>
    /// Kind code of a block letter in a block of <paramref name="district"/> (block table +8: 0 slum, 1 middle class,
    /// 2 center), or null when the cell isn't selectable (F / G lawns and trees by the user's rule, other letters).
    /// </summary>
    public static int? KindOf(char letter, int district) => letter switch
    {
        'B' or 'C' or 'D' => Mathf.Clamp(district, 0, 2),
        _ => null,
    };

    /// <summary>
    /// The plain building cell at a map point, picked by pixels: of the cells around the point, the frontmost (largest
    /// x + y) whose tile picture is opaque there is the one clicked; it counts only when it is a plain building without
    /// a company. So a tree, lawn or road in front selects nothing, and the upper part of a tall building selects it.
    /// </summary>
    public static (Vector2I Cell, int Kind)? At(TileMapLayer map, Vector2 globalPoint)
    {
        if (map == null || !map.HasMeta(MetaCells))
            return null;
        var cells = map.GetMeta(MetaCells).AsGodotDictionary<Vector2I, int>();
        var local = map.ToLocal(globalPoint);
        var under = map.LocalToMap(local);
        for (var depth = 8; depth >= -2; depth--)
            for (var dx = -2; dx <= depth + 2; dx++)
            {
                var cell = under + new Vector2I(dx, depth - dx);
                if (!OpaqueAt(map, cell, local))
                    continue;
                if (!cells.TryGetValue(cell, out var kind))
                    return null;
                if (map.GetCellTileData(cell) is { } td && TileMapGenerator.GetCompany(td) != null)
                    return null;
                return (cell, kind);
            }
        return null;
    }

    private static readonly Dictionary<(TileSet, int), Image> SourceImages = new();
    private static TileSet _imagesOf;

    /// <summary>Is the tile picture of <paramref name="cell"/> opaque at the map-local point?</summary>
    private static bool OpaqueAt(TileMapLayer map, Vector2I cell, Vector2 local)
    {
        var sourceId = map.GetCellSourceId(cell);
        if (sourceId < 0 || map.TileSet.GetSource(sourceId) is not TileSetAtlasSource atlas || atlas.Texture == null)
            return false;
        var coords = map.GetCellAtlasCoords(cell);
        var region = atlas.GetTileTextureRegion(coords);
        var td = map.GetCellTileData(cell);
        // A tile picture is drawn centred on its cell, moved by −texture_origin.
        var topLeft = map.MapToLocal(cell) - (Vector2)region.Size / 2 - (Vector2)(td?.TextureOrigin ?? Vector2I.Zero);
        var p = (Vector2I)(local - topLeft).Floor();
        if (p.X < 0 || p.Y < 0 || p.X >= region.Size.X || p.Y >= region.Size.Y)
            return false;
        if (_imagesOf != map.TileSet)
        {
            SourceImages.Clear(); // a new map (episode): drop the old pictures
            _imagesOf = map.TileSet;
        }
        var key = (map.TileSet, sourceId);
        if (!SourceImages.TryGetValue(key, out var image))
            SourceImages[key] = image = atlas.Texture.GetImage();
        return image != null && image.GetPixelv(region.Position + p).A > 0.5f;
    }

    /// <summary>Moves the shared node to <paramref name="cell"/>.</summary>
    public void Place(TileMapLayer map, Vector2I cell, int kind)
    {
        Cell = cell;
        Kind = kind;
        Position = map.MapToLocal(cell);
    }
}
