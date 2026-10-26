using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace Capone;

/// <summary>
/// Texts of the bottom panel, drawn like the original: the finance block (sub_40A245) and the selected object's info
/// column (sub_40A997). Coordinates are the original 800×600 screen positions minus the panel top (y 380).
/// </summary>
public partial class PanelInfo : Control
{
    public const int PanelTop = 380;
    private const int InfoX = 0x140, InfoWidth = 0xA5;
    private const int FinanceX = 0x258, FinanceWidth = 0x69;

    /// <summary>sub_40F126 colours 0–4: green, red, yellow (default), light green, brown.</summary>
    private static readonly Color[] TextColors =
    {
        Color.Color8(0, 255, 0), Color.Color8(214, 74, 74), Color.Color8(255, 247, 123),
        Color.Color8(140, 239, 198), Color.Color8(82, 66, 24),
    };
    private static readonly Color ShadowColor = Color.Color8(16, 16, 16);

    /// <summary>Window fonts (sub_41A0xx, all Arial): cell heights 14, 16, 24, 16, 28 at weights 400, 500, 700, 700, 700.</summary>
    private static readonly (int Height, int Weight)[] FontSpecs = { (14, 400), (16, 500), (24, 700), (16, 700), (28, 700) };
    private readonly Font[] _fonts = new Font[FontSpecs.Length];
    private double _refresh;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        for (var i = 0; i < FontSpecs.Length; i++)
            _fonts[i] = new SystemFont { FontNames = new[] { "Arial" }, FontWeight = FontSpecs[i].Weight };
    }

    public override void _Process(double delta)
    {
        _refresh -= delta / Engine.TimeScale;
        if (_refresh > 0)
            return;
        _refresh = 0.2;
        QueueRedraw();
    }

    // Godot sizes fonts by em; a Windows cell height is ascent + descent ≈ em × 2288 / 2048 for Arial.
    private static int FontSize(int font) => Mathf.RoundToInt(FontSpecs[font].Height * 2048f / 2288f);

    /// <summary>sub_40F126: shadow at +2,+2, then the text; cut to <paramref name="width"/> pixels.</summary>
    private void Text(string text, int x, int screenY, int width, int font, int color)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var f = _fonts[font];
        var size = FontSize(font);
        while (text.Length > 1 && f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > width)
            text = text[..^1];
        var pos = new Vector2(x, screenY - PanelTop + f.GetAscent(size));
        DrawString(f, pos + new Vector2(2, 2), text, HorizontalAlignment.Left, -1, size, ShadowColor);
        DrawString(f, pos, text, HorizontalAlignment.Left, -1, size, TextColors[Mathf.Clamp(color, 0, TextColors.Length - 1)]);
    }

    private static string T(int index) => OriginalTexts.Instance.Get(5, index);

    public override void _Draw()
    {
        var player = Teams.GetCurrentPlayerTeam();
        if (player?.Org != null)
            DrawFinance(player.Org);
        var selected = GuiGameback.Instance?.SelectedNode;
        if (selected != null && IsInstanceValid(selected))
            DrawInfo(selected, player);
    }

    /// <summary>sub_40A245: cash (HQ vt+60h) and weekly net income (vt+64h).</summary>
    private void DrawFinance(Organisation org)
    {
        var cash = org.Cash >= 20_000_000 ? Money(org.Cash / 1000) + "K" : Money(org.Cash);
        Text(cash, FinanceX, 0x18A, FinanceWidth, 2, 2);
        Text(Money(org.NetIncome), FinanceX, 0x1A9, FinanceWidth, 3, 2);
    }

    /// <summary>Relation colour index (object vt+E4h): own green, enemy red, ally yellow, nobody light green.</summary>
    private static int RelationColor(Team owner, Team player)
    {
        if (owner == null || owner == Teams.NeutralTeam)
            return 3;
        if (owner == player)
            return 0;
        return player != null && player.GetTargetRelation(owner.TeamColor) == Teams.TeamRelation.Friend ? 2 : 1;
    }

    /// <summary>sub_40A997.</summary>
    private void DrawInfo(Node2D node, Team player)
    {
        var owner = node switch { Unit u => u.OwnerTeam, Company c => c.OwnerTeam, _ => null };
        var relation = RelationColor(owner, player);
        var own = owner != null && owner == player;

        switch (node)
        {
            case Company { IsHq: true } hq:
                Text(hq.OwnerTeam?.PlayerName ?? hq.KindName, InfoX, 0x18B, InfoWidth, 2, relation);
                if (own)
                {
                    Text(T(25), InfoX, 0x1A3, InfoWidth, 3, 0);
                    int[] lines = { 0x1B2, 0x1C1, 0x1D0, 0x1DF };
                    for (var type = 1; type <= 4; type++)
                        Text(CFormat(T(26 + type), player.Org.MarketShare[type] * 100), InfoX, lines[type - 1], InfoWidth, 1, 0);
                }
                else if (relation is 1 or 2)
                    Text(T(relation == 1 ? 31 : 32), InfoX, 0x1A3, InfoWidth, 3, relation);
                break;
            case Company company:
                Text(company.CompanyName, InfoX, 0x18B, InfoWidth, 2, relation);
                Text(CompanyKind(company), InfoX, 0x1A3, InfoWidth, 3, relation);
                if (own && company.IsEconomic)
                {
                    Text(CFormat(T(34), Money(company.Income())), InfoX, 0x1B5, InfoWidth, 1, 0);
                    var type = (int)company.BusinessType;
                    if (type != 5)
                        Text(CFormat(T(35), Money(company.Uncollected)), InfoX, 0x1C7, InfoWidth, 1, 0);
                    if (type is >= 1 and <= 4)
                        Text(CFormat(T(36), player.Org.MarketShare[type] * 100), InfoX, 0x1D6, InfoWidth, 1, 0);
                }
                break;
            case Unit unit:
                Text(unit.DisplayName, InfoX, 0x18B, InfoWidth, 2, relation);
                Text(UnitKind(unit), InfoX, 0x1A3, InfoWidth, 3, relation);
                if (own)
                {
                    if (unit.Salary != 0)
                        Text(CFormat(T(33), Money(unit.Salary)), InfoX, 0x1B5, InfoWidth, 1, 0);
                    if (unit.UnitType == UnitCommon.UnitType.Collector)
                        Text(CFormat(T(37), Money(unit.CarriedMoney)), InfoX, 0x1C7, InfoWidth, 1, 0);
                    else if (unit.UnitType == UnitCommon.UnitType.Thief && unit.CarriedMoney > 0)
                        Text(CFormat(T(38), Money(unit.CarriedMoney)), InfoX, 0x1C7, InfoWidth, 1, 0);
                }
                break;
        }

        if (!own && owner != null && owner != Teams.NeutralTeam)
            Text(CFormat(T(39), owner.PlayerName), InfoX, 0x1B5, InfoWidth, 1, relation == 2 ? 2 : 1);

        DrawHpBar(node);
    }

    /// <summary>HP bar under the avatar: black frame 233..318 × 493..502, an 81-pixel bar, green > 50 %, yellow > 25 %, else red.</summary>
    private void DrawHpBar(Node2D node)
    {
        var (hp, max) = node switch
        {
            Unit u => (u.CanBeAttacked?.HP ?? 0, u.CanBeAttacked?.MaxHP ?? 0),
            Company c => (c.Hp, c.MaxHp),
            _ => (0, 0),
        };
        var percent = max == 0 ? 100 : Mathf.Clamp(hp * 100 / max, 0, 100);
        var color = percent > 50 ? Colors.Lime : percent > 25 ? Colors.Yellow : Colors.Red;
        DrawRect(new Rect2(0xE9, 0x1ED - PanelTop, 0x13E - 0xE9, 0x1F6 - 0x1ED), Colors.Black);
        DrawRect(new Rect2(0xEB, 0x1EF - PanelTop, percent * 81 / 100, 0x1F4 - 0x1EF), color);
    }

    private static string CompanyKind(Company company)
    {
        if (company.IsPoliceStation || company.Entry == null)
            return company.KindName;
        return (int)company.BusinessType switch
        {
            0 => T(15 + Mathf.Clamp(company.Block?.District ?? 3, 0, 3)),
            var t and >= 1 and <= 5 => T(18 + t),
            _ => T(24),
        };
    }

    private static string UnitKind(Unit unit)
    {
        var index = unit.UnitType switch
        {
            UnitCommon.UnitType.Thug => 1, UnitCommon.UnitType.Pistol => 2, UnitCommon.UnitType.Thompson => 3,
            UnitCommon.UnitType.Terrorist => 4, UnitCommon.UnitType.Sniper => 5, UnitCommon.UnitType.Collector => 6,
            UnitCommon.UnitType.Cadillac => 7, UnitCommon.UnitType.Shotgun => 8, UnitCommon.UnitType.Hitman => 9,
            UnitCommon.UnitType.Thief => 10, UnitCommon.UnitType.Policeman => 13, UnitCommon.UnitType.Godfather => 87,
            _ => 11,
        };
        return T(index);
    }

    /// <summary>sub_408AD0: the number with its digits grouped by spaces ("1 234 567").</summary>
    public static string Money(long value)
    {
        var digits = System.Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
                sb.Append(' ');
            sb.Append(digits[i]);
        }
        return value < 0 ? "-" + sb : sb.ToString();
    }

    private static readonly Regex FormatSpec = new(@"%(%|[-+ 0#]*\d*(?:\.(\d+))?(?:l|h)?([sdif]))");

    /// <summary>Enough of printf for the original texts: %s, %d/%ld, %N.Mf and %%.</summary>
    public static string CFormat(string format, params object[] args)
    {
        var next = 0;
        return FormatSpec.Replace(format ?? "", m =>
        {
            if (m.Groups[1].Value == "%")
                return "%";
            var arg = next < args.Length ? args[next++] : null;
            return m.Groups[3].Value switch
            {
                "f" => System.Convert.ToDouble(arg, CultureInfo.InvariantCulture)
                    .ToString("F" + (m.Groups[2].Success ? m.Groups[2].Value : "6"), CultureInfo.InvariantCulture),
                _ => System.Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "",
            };
        });
    }
}
