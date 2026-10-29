using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The fog of war as the original draws it (ground tile draw sub_438C20, class 2 ground only: R road and S pavement;
/// building blocks never get fog — user): right after a cell's own tile, an unseen cell gets Chicago.tile 49h
/// "DARK SQUARE" (a dithered black diamond from CHICAGO2.bmp), and a seen cell gets the edge piece of every unseen
/// neighbour — 9C8h–9CFh "DARK NORTH, NORTHEAST, EAST, SOUTHEAST, SOUTH, SOUTHWEST, WEST, NORTHWEST", named by the
/// neighbour's direction on screen — so the fog slides into the seen area instead of stopping at a hard border.
/// The pieces are chosen by the neighbour's screen direction on our map (its grid is oriented differently from the
/// EXE's map coordinates).
/// Here: a y-sorted layer after the map, one tile per cell; the edge combinations are composed on demand. A fog tile
/// shares the draw level of the cell's own tile (flat ground below the units, tall pictures with them). Our cells
/// sit on half-pixel rows (9.5 px apart), so per-tile dithering would not line up: the full square is drawn as a
/// solid diamond (marked red) and a shader dithers it on a checkerboard in map pixels; edge pieces keep their dots.
/// </summary>
public partial class FogTiles : TileMapLayer
{
    public const int SquareTile = 0x49;
    public const int FirstEdgeTile = 0x9C8;
    /// <summary>Code of the full fog square; 1..255 are edge masks.</summary>
    public const int Square = 256;
    private const int Columns = 16;
    private static readonly Vector2I Cell = new(40, 20);

    private readonly Vector2I[] _neighbours = new Vector2I[8];
    private TileMapLayer _map;
    private HashSet<Vector2I> _used;
    private Image _sheet;
    private Rect2I _squareRect;
    private readonly Rect2I[] _edgeRects = new Rect2I[8];
    private Image _atlasImage;
    private ImageTexture _atlasTexture;
    private TileSetAtlasSource _source;
    private readonly Dictionary<int, Vector2I> _codeCoords = new();
    private readonly Dictionary<Vector2I, int> _shown = new();

    public FogTiles() => Name = "FogTiles";

    public void Init(TileMapLayer map)
    {
        _map = map;
        // Only ground (road, pavement) gets fog and counts as fogged.
        _used = new HashSet<Vector2I>();
        foreach (var meta in new[] { TileMapGenerator.MetaRoad, TileMapGenerator.MetaSand })
            if (map.HasMeta(meta))
                _used.UnionWith(map.GetMeta(meta).AsGodotArray<Vector2I>());
        YSortEnabled = true;
        TextureFilter = TextureFilterEnum.Nearest;
        // Edge piece k = compass direction k (N, NE, E, SE, S, SW, W, NW) of the neighbour on screen.
        var centre = map.MapToLocal(Vector2I.Zero);
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var v = map.MapToLocal(new Vector2I(dx, dy)) - centre;
                var angle = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(v.X, -v.Y)), 360f); // 0 = up (north), clockwise
                _neighbours[Mathf.PosMod(Mathf.RoundToInt(angle / 45f), 8)] = new Vector2I(dx, dy);
            }

        var tiles = OriginalData.Instance.Tiles;
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("CHICAGO2.BMP", out var bmp) && bmp.Body != null
            && tiles.Tiles.Count > FirstEdgeTile + 7)
        {
            _sheet = bmp.Body;
            Rect2I Rect(int t) => new(tiles.Tiles[t].Pairs[0], tiles.Tiles[t].Size);
            _squareRect = Rect(SquareTile);
            for (var k = 0; k < 8; k++)
                _edgeRects[k] = Rect(FirstEdgeTile + k);
        }

        _atlasImage = Image.CreateEmpty(Cell.X * Columns, Cell.Y * (Square / Columns + 2), false, Image.Format.Rgba8);
        _atlasTexture = ImageTexture.CreateFromImage(_atlasImage);
        _source = new TileSetAtlasSource { Texture = _atlasTexture, TextureRegionSize = Cell };
        var set = new TileSet
        {
            TileShape = map.TileSet.TileShape,
            TileSize = map.TileSet.TileSize,
            TileLayout = map.TileSet.TileLayout,
            TileOffsetAxis = map.TileSet.TileOffsetAxis,
        };
        set.AddSource(_source, 0);
        TileSet = set;
        Material = new ShaderMaterial { Shader = new Shader { Code = DitherShader } };
    }

    /// <summary>Red pixels: solid fog, dithered on the map's pixel grid; others drawn as they are.</summary>
    private const string DitherShader = @"
shader_type canvas_item;
varying vec2 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
void fragment() {
    vec4 c = texture(TEXTURE, UV);
    if (c.a < 0.5) discard;
    if (c.r > 0.5) {
        if (mod(floor(world.x) + floor(world.y), 2.0) < 0.5) discard;
        COLOR = vec4(0.0, 0.0, 0.0, 1.0);
    } else {
        COLOR = c;
    }
}";

    /// <summary>Shows the fog for the cells seen now (<paramref name="enabled"/> false: no fog at all).</summary>
    public void Refresh(HashSet<Vector2I> visible, bool enabled)
    {
        if (_sheet == null)
            return;
        var changed = false;
        foreach (var cell in _used)
        {
            var code = 0;
            if (enabled)
            {
                if (!visible.Contains(cell))
                    code = Square;
                else
                    for (var k = 0; k < 8; k++)
                    {
                        var n = cell + _neighbours[k];
                        if (_used.Contains(n) && !visible.Contains(n))
                            code |= 1 << k;
                    }
            }
            if (_shown.GetValueOrDefault(cell) == code)
                continue;
            _shown[cell] = code;
            if (code == 0)
            {
                EraseCell(cell);
                continue;
            }
            changed |= Ensure(code);
            // Same draw level as the cell's own picture (flat ground z −1, tall pictures z 0).
            var tall = (_map.GetCellTileData(cell)?.ZIndex ?? 0) != TileMapGenerator.GroundZ;
            SetCell(cell, 0, _codeCoords[code], tall ? 1 : 0);
        }
        if (changed)
            _atlasTexture.Update(_atlasImage);
    }

    /// <summary>The DARK SQUARE's diamond filled solid (red marker), one pixel larger to close the half-pixel gaps.</summary>
    private void PaintSolidSquare(Vector2I at)
    {
        var w = _squareRect.Size.X;
        var h = _squareRect.Size.Y;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = Mathf.Abs(x + 0.5f - w / 2f) / (w / 2f);
                var dy = Mathf.Abs(y + 0.5f - h / 2f) / (h / 2f);
                if (dx + dy <= 1.08f)
                    _atlasImage.SetPixel(at.X + x, at.Y + y, new Color(1, 0, 0, 1));
            }
    }

    /// <summary>Composes the picture for a code the first time it is needed. Returns whether the atlas changed.</summary>
    private bool Ensure(int code)
    {
        if (_codeCoords.ContainsKey(code))
            return false;
        var index = _codeCoords.Count;
        var coords = new Vector2I(index % Columns, index / Columns);
        var at = coords * Cell;
        if (code == Square)
            PaintSolidSquare(at);
        else
            for (var k = 0; k < 8; k++)
                if ((code & (1 << k)) != 0)
                    _atlasImage.BlendRect(_sheet, _edgeRects[k], at);
        _codeCoords[code] = coords;
        _source.CreateTile(coords);
        _source.GetTileData(coords, 0).ZIndex = TileMapGenerator.GroundZ;
        _source.CreateAlternativeTile(coords, 1);
        _source.GetTileData(coords, 1).ZIndex = 0;
        return true;
    }
}
