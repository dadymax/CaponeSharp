using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The menu's "Альянсы" window (main window +944h): built by sub_45B796, loaded by sub_45BA00 (Pics\AllyWin.bmp,
/// colour key = the pixel at (349, 397)), drawn by sub_45BA83, mouse sub_45BD30 (down) / sub_45C0A0 (move) /
/// sub_45BF06 (up).
/// <list type="bullet">
/// <item>AllyWin's top 400 × 380 at (200, 150): four squares — columns "Не сотрудничаем / Сотрудничаем" (the local
/// player's stance), rows "Не сотрудничают / Сотрудничают" (theirs). Relation (player vt+0E0h): 1 = not cooperating,
/// 2 = cooperating.</item>
/// <item>Each other player in the game is a hand (210, 380, 30, 30) — (240, 380) while dragged — with the name centred
/// under it at (x + 15, y + 30), width 150, font 1, green. Start places: per square the next of 13 slots (tables
/// 0x48E010 / 0x48E078 / 0x48E0E0 / 0x48E148, cycling).</item>
/// <item>A hand can be dragged sideways only: its row stays (y within the row's square), x anywhere over both columns;
/// dropped, it snaps into the square of the column it is in (bounds table 0x48E1E8; the middle at x 378, y 329).</item>
/// <item>"Изменить" (250, 498)–(350, 523) posts event sub_432AA0 per player — 1 for the left column, 2 for the right —
/// which sets the local player's own stance (vt+0DCh); theirs does not change. "Вернуться" (450, 498)–(550, 523)
/// just closes. While the mouse is over one, the picture's (0 / 100, 380, 100, 21); OverButton on entering,
/// ButtonClick on a click.</item>
/// </list>
/// </summary>
public partial class AllianceWindow : Control
{
    private const int MidX = 378, MidY = 329, Hand = 30;
    // Squares (x0, y0, x1, y1): top-left, top-right, bottom-left, bottom-right (0x48E1E8).
    private static readonly Rect2I[] Squares =
    {
        new(228, 197, 128, 107), new(403, 197, 128, 107), new(228, 345, 128, 107), new(403, 345, 128, 107),
    };
    // Start slots of each square: 0x48E010 (neither cooperates), 0x48E078 (only we), 0x48E0E0 (only they), 0x48E148 (both).
    private static readonly Vector2I[][] Slots =
    {
        new Vector2I[] { new(238, 197), new(238, 247), new(238, 297), new(288, 197), new(288, 247), new(288, 297), new(338, 197), new(338, 247), new(338, 297), new(263, 223), new(263, 273), new(313, 223), new(313, 273) },
        new Vector2I[] { new(413, 197), new(413, 247), new(413, 297), new(463, 197), new(463, 247), new(463, 297), new(513, 197), new(513, 247), new(513, 297), new(438, 223), new(438, 273), new(488, 223), new(488, 273) },
        new Vector2I[] { new(238, 345), new(238, 395), new(238, 445), new(288, 345), new(288, 395), new(288, 445), new(338, 345), new(338, 395), new(338, 445), new(263, 370), new(263, 420), new(313, 370), new(313, 420) },
        new Vector2I[] { new(413, 345), new(413, 395), new(413, 445), new(463, 345), new(463, 395), new(463, 445), new(513, 345), new(513, 395), new(513, 445), new(438, 370), new(438, 420), new(488, 370), new(488, 420) },
    };

    private sealed class Token
    {
        public Team Player;
        public Vector2I Position;
    }

    private readonly List<Token> _tokens = new();
    private Texture2D _picture;
    private Token _dragged;
    private int _hover = -1; // +4: 0 "Изменить", 1 "Вернуться"

    /// <summary>The relation from <paramref name="from"/> to <paramref name="to"/> as the EXE stores it: 1 or 2.</summary>
    public static int Stance(Team from, Team to) =>
        from.GetTargetRelation(to.TeamColor) == Teams.TeamRelation.Friend ? 2 : 1;

    /// <summary>The square (0..3) a player starts in: row = their stance to us, column = ours to them.</summary>
    public static int SquareOf(Team local, Team other) =>
        (Stance(other, local) == 2 ? 2 : 0) + (Stance(local, other) == 2 ? 1 : 0);

    public AllianceWindow(Team local, IEnumerable<Team> others)
    {
        Name = "AllianceWindow";
        var next = new int[4];
        foreach (var other in others)
        {
            var square = SquareOf(local, other);
            _tokens.Add(new Token { Player = other, Position = Slots[square][next[square]] });
            next[square] = (next[square] + 1) % Slots[square].Length;
        }
    }

    /// <summary>"Изменить" (event sub_432AE0 → vt+0DCh): the local player's stance from the column of each hand.</summary>
    public static void Apply(Team local, IEnumerable<(Team Player, bool Cooperate)> stances)
    {
        foreach (var (player, cooperate) in stances)
        {
            if (player.Org is not { Eliminated: false })
                continue;
            var relation = cooperate ? Teams.TeamRelation.Friend : Teams.TeamRelation.Enemy;
            if (local.GetTargetRelation(player.TeamColor) == relation)
                continue;
            local.SetTargetRelation(player.TeamColor, relation);
            GD.Print($"AllianceWindow: {local.PlayerName} {(cooperate ? "cooperates with" : "stops cooperating with")} {player.PlayerName}");
            if (Engine.GetMainLoop() is not SceneTree tree)
                continue;
            foreach (var unit in tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>())
                if (unit.OwnerTeam == player)
                    unit.RefreshOwnerRelation();
            foreach (var company in tree.GetNodesInGroup(Company.GroupName).OfType<Company>())
                if (company.OwnerTeam == player)
                    company.RefreshOwnerRelation();
        }
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("ALLYWIN.BMP", out var bmp) && bmp.Body != null)
        {
            var image = (Image)bmp.Body.Duplicate();
            image.Convert(Image.Format.Rgba8);
            var key = image.GetPixel(349, 397);
            for (var y = 380; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                    if (image.GetPixel(x, y).IsEqualApprox(key))
                        image.SetPixel(x, y, Colors.Transparent);
            _picture = ImageTexture.CreateFromImage(image);
        }
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(200, 150, 400, 380).HasPoint(point);

    private static int ButtonAt(Vector2 p) =>
        p.Y > 498 && p.Y < 523 ? p.X > 250 && p.X < 350 ? 0 : p.X > 450 && p.X < 550 ? 1 : -1 : -1;

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                if (_dragged != null)
                {
                    // sub_45C0A0: the hand follows the mouse (centred), kept in its row.
                    var row = _dragged.Position.Y > MidY ? Squares[2] : Squares[0];
                    _dragged.Position = new Vector2I(
                        Mathf.Clamp((int)motion.Position.X - 15, Squares[0].Position.X, Squares[1].End.X),
                        Mathf.Clamp((int)motion.Position.Y - 15, row.Position.Y, row.End.Y));
                    QueueRedraw();
                    break;
                }
                var over = ButtonAt(motion.Position);
                if (over >= 0 && over != _hover)
                    Sound.Play("OverButton.wav");
                if (over != _hover)
                {
                    _hover = over;
                    QueueRedraw();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                AcceptEvent();
                if (click.Pressed)
                    Press(click.Position);
                else
                    Drop(click.Position);
                break;
        }
    }

    private void Press(Vector2 p)
    {
        switch (ButtonAt(p))
        {
            case 0:
                Sound.Play("ButtonClick.wav");
                var local = Teams.GetCurrentPlayerTeam();
                if (local != null)
                    Apply(local, _tokens.Select(t => (t.Player, t.Position.X >= MidX)));
                Close();
                return;
            case 1:
                Sound.Play("ButtonClick.wav");
                Close();
                return;
        }
        _dragged = _tokens.FirstOrDefault(t =>
            t.Position.X < p.X && p.X < t.Position.X + Hand && t.Position.Y < p.Y && p.Y < t.Position.Y + Hand);
        QueueRedraw();
    }

    /// <summary>sub_45BF06: snap into the square of the column the hand is over.</summary>
    private void Drop(Vector2 p)
    {
        if (_dragged == null)
            return;
        var bottom = _dragged.Position.Y > MidY;
        var right = _dragged.Position.X > MidX;
        var square = Squares[(bottom ? 2 : 0) + (right ? 1 : 0)];
        _dragged.Position = new Vector2I(
            Mathf.Clamp((int)p.X - 15, square.Position.X, square.End.X),
            Mathf.Clamp((int)p.Y - 15, square.Position.Y, square.End.Y));
        _dragged = null;
        QueueRedraw();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Keycode: Key.Escape })
            return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        if (!IsQueuedForDeletion())
            QueueFree();
    }

    /// <summary>Test hook: put the hand of player <paramref name="index"/> into column 0 / 1.</summary>
    public void MoveTo(int index, bool cooperate)
    {
        if (index < 0 || index >= _tokens.Count)
            return;
        var token = _tokens[index];
        var square = Squares[(token.Position.Y > MidY ? 2 : 0) + (cooperate ? 1 : 0)];
        token.Position = new Vector2I(square.Position.X + 10, token.Position.Y);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_picture == null)
            return;
        DrawTextureRectRegion(_picture, new Rect2(200, 150, 400, 380), new Rect2(0, 0, 400, 380));
        if (_hover == 0)
            DrawTextureRectRegion(_picture, new Rect2(250, 498, 100, 21), new Rect2(0, 380, 100, 21));
        else if (_hover == 1)
            DrawTextureRectRegion(_picture, new Rect2(450, 498, 100, 21), new Rect2(100, 380, 100, 21));
        foreach (var token in _tokens)
            DrawTextureRectRegion(_picture, new Rect2(token.Position, new Vector2(Hand, Hand)),
                new Rect2(token == _dragged ? 240 : 210, 380, Hand, Hand));
        foreach (var token in _tokens)
            OriginalFonts.DrawScreen(this, token.Player.PlayerName, token.Position.X + 15, token.Position.Y + 30, 150, 101, 0);
    }
}
