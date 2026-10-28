using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Fog of war for the local player (rules from the user, who played the original; sight rules from the EXE, see
/// <see cref="Vision"/>): the player sees what their own and their allies' units (current vision, unit vt+1ACh) and
/// buildings (4 tiles, company / HQ vt+0E8h; sub_4393C6 marks the tiles) see. Nothing stays explored: when the viewers
/// leave, the fog comes back and other players' units there are hidden. Companies out of sight keep showing their last
/// seen state (<see cref="Company.InFog"/>). Unseen cells are drawn darker.
/// </summary>
public partial class FogOfWar : Node
{
    public const int BuildingSight = 4;
    private const double UpdateSeconds = 0.25;
    private static readonly Color FogTint = new(0.5f, 0.5f, 0.55f);
    /// <summary>Alternative tile id of the darkened copy of every map tile.</summary>
    private const int FogAlternative = 1;

    /// <summary>A setting: off shows everything (as before).</summary>
    public static bool Enabled { get; set; } = true;

    public static FogOfWar Instance { get; private set; }

    private readonly HashSet<Vector2I> _visible = new();
    private readonly HashSet<Vector2I> _fogged = new();
    private readonly Dictionary<Company, (Vector2I Cell, bool[] Grid)> _buildingGrids = new();
    private TileMapLayer _map;
    private List<Vector2I> _cells = new();
    private double _timer;

    public override void _Ready()
    {
        Instance = this;
        _map = GetParent() as TileMapLayer;
        if (_map == null)
            return;
        _cells = _map.GetUsedCells().ToList();
        AddDarkTiles(_map.TileSet);
    }

    /// <summary>
    /// Gives every map tile a darkened alternative (same texture origin, z, and custom data such as nonWalkable and the
    /// company), so a fogged cell — building sprite included — is drawn darker by switching the cell's alternative.
    /// </summary>
    private static void AddDarkTiles(TileSet set)
    {
        if (set == null)
            return;
        var layers = set.GetCustomDataLayersCount();
        for (var s = 0; s < set.GetSourceCount(); s++)
        {
            if (set.GetSource(set.GetSourceId(s)) is not TileSetAtlasSource atlas)
                continue;
            for (var t = 0; t < atlas.GetTilesCount(); t++)
            {
                var coords = atlas.GetTileId(t);
                if (atlas.HasAlternativeTile(coords, FogAlternative))
                    continue;
                atlas.CreateAlternativeTile(coords, FogAlternative);
                var original = atlas.GetTileData(coords, 0);
                var dark = atlas.GetTileData(coords, FogAlternative);
                dark.TextureOrigin = original.TextureOrigin;
                dark.ZIndex = original.ZIndex;
                dark.YSortOrigin = original.YSortOrigin;
                dark.Material = original.Material;
                dark.Modulate = original.Modulate * FogTint;
                for (var l = 0; l < layers; l++)
                    dark.SetCustomDataByLayerId(l, original.GetCustomDataByLayerId(l));
            }
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Is <paramref name="cell"/> seen by the local player (always true with the fog off)?</summary>
    public bool IsVisible(Vector2I cell) => !Enabled || _visible.Contains(cell);

    public int VisibleCount => _visible.Count;

    public string Describe() => $"map cells {_cells.Count}, visible {_visible.Count}, fogged {_fogged.Count}";

    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0)
            return;
        _timer = UpdateSeconds;
        Refresh();
    }

    public void Refresh()
    {
        if (_map == null)
            return;
        var tree = GetTree();
        var player = Teams.GetCurrentPlayerTeam();
        var units = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => IsInstanceValid(u) && !u.IsDead).ToList();
        var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(IsInstanceValid).ToList();
        var enabled = Enabled && player != null;

        _visible.Clear();
        if (enabled)
        {
            foreach (var unit in units.Where(u => IsFriendly(player, u.OwnerTeam)))
            {
                var grid = unit.SightGrid(out var center, out var r);
                Add(grid, center, r);
            }
            foreach (var company in companies.Where(c => IsFriendly(player, c.OwnerTeam)))
            {
                var cell = _map.LocalToMap(company.Position);
                if (!_buildingGrids.TryGetValue(company, out var cached) || cached.Cell != cell)
                    _buildingGrids[company] = cached = (cell, Vision.Build(cell, BuildingSight));
                Add(cached.Grid, cell, BuildingSight);
            }
        }

        foreach (var unit in units)
            unit.Visible = !enabled || IsFriendly(player, unit.OwnerTeam) || _visible.Contains(_map.LocalToMap(unit.Position));
        foreach (var company in companies)
            company.InFog = enabled && !_visible.Contains(_map.LocalToMap(company.Position));

        // Darken the unseen cells by switching to the dark alternative tile (only the changes).
        foreach (var cell in _cells)
        {
            var fog = enabled && !_visible.Contains(cell);
            if (fog ? _fogged.Add(cell) : _fogged.Remove(cell))
                _map.SetCell(cell, _map.GetCellSourceId(cell), _map.GetCellAtlasCoords(cell), fog ? FogAlternative : 0);
        }
        foreach (var company in companies)
            company.Modulate = company.InFog ? FogTint : Colors.White;
    }

    private static bool IsFriendly(Team player, Team owner) =>
        owner != null && player.GetTargetRelation(owner.TeamColor) is Teams.TeamRelation.Self or Teams.TeamRelation.Friend;

    private void Add(bool[] grid, Vector2I center, int r)
    {
        var size = 2 * r + 1;
        for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
                if (grid[(dy + r) * size + dx + r])
                    _visible.Add(center + new Vector2I(dx, dy));
    }
}
