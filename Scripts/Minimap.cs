using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The minimap in the panel's bottom-left corner (sub_40DD60, called from the panel draw sub_410110): a 200 × 200 box
/// at (10, 390)..(210, 590) (dword_476BA8..B4).
/// <list type="bullet">
/// <item>The map layer is drawn once into its own surface (again on a full redraw; here every game hour) and
/// blitted with a colour key, so the panel picture shows around it. One cell is two pixels side by side at the
/// cell's screen position / (20, 10) — tile width / 2, row height — centred in the box (sub_40E378); when width +
/// height of the map exceed 200 every other cell is used (sub_40DF43, half scale). Road and pavement are grey
/// (808080), police stations blue (FF0000 BGR), everything else silver (C0C0C0); the right pixel of a cell not seen
/// lately (vt+0DCh) is black.</item>
/// <item>Then on every redraw (sub_40EBD0 / sub_40E665): companies coloured by their relation — own green, enemy
/// red, ally yellow, nobody's blue; units seen by the player — own green (collectors dark green), allies yellow,
/// enemies red, the police cyan (a police car even unseen); anything fighting (vt+0D4h → vt+11Ch) blinks black every
/// other redraw (dword_476F50); and the view as a white rectangle (view position / (40 / 2, 10)).</item>
/// </list>
/// A click or drag in the box moves the view there.
/// </summary>
public partial class Minimap : Control
{
    public const int Box = 200;
    private static readonly Vector2 BoxTopLeft = new(10, 390 - PanelInfo.PanelTop);

    private static readonly Color Road = Color.Color8(0x80, 0x80, 0x80);
    private static readonly Color Building = Color.Color8(0xC0, 0xC0, 0xC0);
    private static readonly Color Blue = Color.Color8(0, 0, 0xFF);
    private static readonly Color Red = Color.Color8(0xFF, 0, 0);
    private static readonly Color Green = Color.Color8(0, 0xFF, 0);
    private static readonly Color DarkGreen = Color.Color8(0, 0x80, 0);
    private static readonly Color Yellow = Color.Color8(0xFF, 0xFF, 0);
    private static readonly Color Cyan = Color.Color8(0, 0xFF, 0xFF);
    private static readonly Color Black = Color.Color8(0, 0, 0);
    private static readonly Color White = Color.Color8(0xFF, 0xFF, 0xFF);

    private Image _image;
    private ImageTexture _texture;
    private TileMapLayer _map;
    private Vector2 _origin; // map-local pixel of the box's top-left
    private int _scale = 1;  // 1 full, 2 half
    private Vector2 _cellPixel; // map-local pixels per minimap pixel (x: tile width / 2, y: row height)
    private long _builtHour = -1;
    private double _redraw;
    private bool _blink;
    private bool _dragging;

    public override void _Ready()
    {
        Position = BoxTopLeft;
        Size = new Vector2(Box, Box);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        _image = Image.CreateEmpty(Box, Box, false, Image.Format.Rgba8);
    }

    private bool Setup()
    {
        var map = Main.Instance?.Map;
        if (map == null || !IsInstanceValid(map))
            return false;
        if (map == _map)
            return true;
        _map = map;
        _builtHour = -1;
        var tile = (Vector2)map.TileSet.TileSize;
        var cells = map.GetUsedCells();
        if (cells.Count == 0)
            return false;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in cells)
        {
            var p = map.MapToLocal(c);
            min = min.Min(p);
            max = max.Max(p);
        }
        _cellPixel = new Vector2(tile.X / 2, tile.Y / 2);
        var size = (max - min) / _cellPixel + new Vector2(2, 1);
        // sub_40DD60: full scale when width + height (the diamond's size in pixels) fit into the box.
        _scale = size.X <= Box && size.Y <= Box ? 1 : 2;
        _cellPixel *= _scale;
        size = (max - min) / _cellPixel + new Vector2(2, 1);
        _origin = min - (new Vector2(Box, Box) - size) / 2 * _cellPixel;
        return true;
    }

    private Vector2 ToBox(Vector2 mapLocal) => ((mapLocal - _origin) / _cellPixel).Floor();

    public override void _Process(double delta)
    {
        if (!Setup())
            return;
        var hour = (GameClock.Instance?.Minute ?? 0) / GameClock.MinutesPerHour;
        if (hour != _builtHour)
        {
            _builtHour = hour;
            BuildMapLayer();
        }
        // The panel redraw timer is about 100 ms (sub_4198A0); the blink flips on every minimap draw.
        _redraw -= delta / Mathf.Max(Engine.TimeScale, 0.01);
        if (_redraw > 0)
            return;
        _redraw = 0.1;
        _blink = !_blink;
        QueueRedraw();
    }

    private void BuildMapLayer()
    {
        _image.Fill(new Color(0, 0, 0, 0));
        var fog = FogOfWar.Instance;
        var stations = Main.Instance.GetTree().GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => IsInstanceValid(c) && c.IsPoliceStation).Select(c => c.Cell).ToHashSet();
        foreach (var cell in _map.GetUsedCells())
        {
            var p = ToBox(_map.MapToLocal(cell));
            var x = (int)p.X;
            var y = (int)p.Y;
            if (x < 0 || y < 0 || x + 1 >= Box || y >= Box)
                continue;
            var seen = fog == null || fog.IsVisible(cell);
            Color left, right;
            if (stations.Contains(cell))
                left = right = Blue; // class 0Bh: both pixels, seen or not
            else
            {
                left = MoveMap.IsRoadOrPavement(cell) ? Road : Building;
                right = seen ? left : Black;
            }
            _image.SetPixel(x, y, left);
            _image.SetPixel(x + 1, y, right);
        }
        if (_texture == null)
            _texture = ImageTexture.CreateFromImage(_image);
        else
            _texture.Update(_image);
    }

    public override void _Draw()
    {
        if (_texture == null || _map == null)
            return;
        DrawTexture(_texture, Vector2.Zero);
        var player = Teams.GetCurrentPlayerTeam();
        var tree = GetTree();

        foreach (var company in tree.GetNodesInGroup(Company.GroupName).OfType<Company>())
        {
            if (!IsInstanceValid(company) || !company.IsEconomic || !company.Discovered)
                continue;
            var color = Relation(player, company.ShownOwner) switch
            {
                Teams.TeamRelation.Self => Green,
                Teams.TeamRelation.Friend => Yellow,
                Teams.TeamRelation.Enemy => Red,
                _ => Blue,
            };
            if (_blink && company.CanBeAttacked?.RecentlyHit == true)
                color = Black;
            Dot(company.Position, color);
        }

        var fog = FogOfWar.Instance;
        foreach (var unit in tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>())
        {
            if (!IsInstanceValid(unit) || unit.IsDead || unit.IsArrested || unit.InsideBuilding || unit.Vehicle != null)
                continue;
            var own = unit.OwnerTeam != null && unit.OwnerTeam == player;
            var seen = own || fog == null || fog.IsVisible(unit.Cell);
            Color color;
            if (!seen)
            {
                if (unit.UnitType != UnitCommon.UnitType.PoliceCar)
                    continue;
                color = Cyan;
            }
            else if (unit.OwnerTeam == null)
                color = Cyan;
            else if (own)
                color = unit.UnitType == UnitCommon.UnitType.Collector ? DarkGreen : Green;
            else
                color = Relation(player, unit.OwnerTeam) == Teams.TeamRelation.Friend ? Yellow : Red;
            if (_blink && unit.IsFighting)
                color = Black;
            Dot(unit.Position, color);
        }

        // The view: white rectangle.
        if (ViewRect() is { } view)
        {
            var a = ToBox(view.Position);
            var b = ToBox(view.End);
            var r = new Rect2(a, b - a).Intersection(new Rect2(0, 0, Box, Box));
            if (r.Size.X > 0 && r.Size.Y > 0)
                DrawRect(r, White, false, 1);
        }
    }

    private static Teams.TeamRelation Relation(Team player, Team owner)
    {
        if (owner == null || player == null)
            return Teams.TeamRelation.Neutral;
        return player.GetTargetRelation(owner.TeamColor);
    }

    private void Dot(Vector2 mapLocal, Color color)
    {
        var p = ToBox(mapLocal);
        if (p.X < 0 || p.Y < 0 || p.X + 1 >= Box || p.Y >= Box)
            return;
        DrawRect(new Rect2(p, new Vector2(2, 1)), color);
    }

    private Camera2D Camera => Main.Instance?.GetNodeOrNull<Camera2D>("Camera2D");

    /// <summary>The map area of the screen (above the panel), in map-local pixels.</summary>
    private Rect2? ViewRect()
    {
        var camera = Camera;
        if (camera == null)
            return null;
        var screen = GetViewportRect().Size;
        var zoom = camera.Zoom;
        var center = _map.ToLocal(camera.GetScreenCenterPosition());
        var topLeft = center - screen / 2 / zoom;
        return new Rect2(topLeft, new Vector2(screen.X, PanelInfo.PanelTop) / zoom);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (button.Pressed)
                    MoveViewTo(button.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                MoveViewTo(motion.Position);
                AcceptEvent();
                break;
        }
    }

    /// <summary>Centres the map area of the screen on the clicked minimap point.</summary>
    private void MoveViewTo(Vector2 boxPoint)
    {
        if (_map == null)
            return;
        CenterView(_map.ToGlobal(_origin + boxPoint * _cellPixel));
        QueueRedraw();
    }

    /// <summary>Centres the map area of the screen (above the panel) on a world point.</summary>
    public static void CenterView(Vector2 global)
    {
        var camera = Main.Instance?.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null)
            return;
        var screen = camera.GetViewportRect().Size;
        // The camera centres the whole window; the map area ends at the panel's top.
        var shift = new Vector2(0, screen.Y / 2 - PanelInfo.PanelTop / 2f) / camera.Zoom;
        camera.GlobalPosition = global + shift;
    }
}
