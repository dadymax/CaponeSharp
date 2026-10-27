using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The six action slots under the avatar (43×43 Chicago.tile buttons; the tile after each icon is its "down" picture).
/// For an own company it shows the menu of sub_421370: a legal company offers "Upgrade to … biz" for the five business
/// types (actions 3–7), which opens the list of up to five catalogue rows with "Back" in the sixth slot (action 8);
/// a business offers "Sell for $%ld" when undamaged. TODO: Repair (action 1, job sub_4289A0), the training-center
/// button, actions on other players' companies (raids, demolition), unit orders.
/// </summary>
public partial class ActionBar : Control
{
    public const int SlotCount = 6;
    public const int SlotSize = 43;
    /// <summary>Panel position of slot 0 (screen 230, 504; table 0x476B18).</summary>
    public static readonly Vector2 Origin = new(230, 504 - PanelInfo.PanelTop);

    private const int IconUpgrade = 0xAD4, IconRepair = 0xAD6, IconTroubleshooting = 0xADA, IconBootlegging = 0xADC,
        IconBack = 0xADE, IconTrainingCenter = 0xD45, IconGambling = 0xAE0, IconEntertainment = 0xAE2, IconSupport = 0xAE4, IconSell = 0xD47;

    /// <summary>"Upgrade to … biz" buttons in menu order (sub_421370): type, icon, text (30, n).</summary>
    private static readonly (OriginalBusinessTable.BusinessType Type, int Icon, int Text)[] TypeButtons =
    {
        (OriginalBusinessTable.BusinessType.Bootlegging, IconBootlegging, 1),
        (OriginalBusinessTable.BusinessType.Entertainment, IconEntertainment, 2),
        (OriginalBusinessTable.BusinessType.Gambling, IconGambling, 3),
        (OriginalBusinessTable.BusinessType.Troubleshooting, IconTroubleshooting, 4),
        (OriginalBusinessTable.BusinessType.Support, IconSupport, 5),
    };

    private readonly TextureButton[] _slots = new TextureButton[SlotCount];
    private readonly Action[] _actions = new Action[SlotCount];
    private readonly string[] _texts = new string[SlotCount];
    private Texture2D _gameButt;
    private static readonly Dictionary<int, Texture2D> IconCache = new();

    /// <summary>Company whose type submenu is open, and the type (original dword_48029C / word_4802A0).</summary>
    private Company _submenuCompany;
    private OriginalBusinessTable.BusinessType _submenuType;
    private string _shown = "";
    private double _refresh;

    public override void _Ready()
    {
        Position = Origin;
        Size = new Vector2(SlotSize * SlotCount, SlotSize);
        MouseFilter = MouseFilterEnum.Ignore;
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = i;
            var button = new TextureButton { Position = new Vector2(SlotSize * i, 0), Size = new Vector2(SlotSize, SlotSize), Visible = false };
            button.Pressed += () => _actions[slot]?.Invoke();
            PanelInfo.HookStatus(button, () => _texts[slot] ?? "");
            AddChild(button);
            _slots[i] = button;
        }
    }

    public override void _Process(double delta)
    {
        _refresh -= delta / Engine.TimeScale;
        if (_refresh > 0)
            return;
        _refresh = 0.2;
        Rebuild(false);
        QueueRedraw();
    }

    private static Texture2D Icon(int tileIndex)
    {
        if (IconCache.TryGetValue(tileIndex, out var texture))
            return texture;
        var tile = OriginalData.Instance.Tiles.GetTileByIndex(tileIndex);
        var images = tile != null ? OriginalData.Instance.GetImageForTile(tile) : null;
        texture = images is { Count: > 0 } ? ImageTexture.CreateFromImage(images[0]) : null;
        IconCache[tileIndex] = texture;
        return texture;
    }

    private void Set(int slot, int icon, string tooltip, Action action)
    {
        var button = _slots[slot];
        button.TextureNormal = Icon(icon);
        button.TextureHover = Icon(icon);
        button.TexturePressed = Icon(icon + 1);
        _texts[slot] = tooltip;
        if (button.IsHovered())
            PanelInfo.StatusText = tooltip;
        button.Visible = true;
        _actions[slot] = action;
    }

    /// <summary>
    /// sub_40A997: while the selected own company runs a conversion job (class 15h) GameButt.bmp's frame (0,0 258×43)
    /// covers the slots at (230, 504) and its coin bar (0,63, 37 high) fills up to 252 px at (233, 507).
    /// </summary>
    public override void _Draw()
    {
        if (GuiGameback.Instance?.SelectedNode is not Company { Busy: true } company || !IsInstanceValid(company)
            || company.OwnerTeam != Teams.GetCurrentPlayerTeam())
            return;
        _gameButt ??= ScreenPictures.Load("GAMEBUTT.BMP");
        if (_gameButt == null)
            return;
        DrawTextureRectRegion(_gameButt, new Rect2(0, 0, 258, 43), new Rect2(0, 0, 258, 43));
        var w = Mathf.RoundToInt(company.BuildProgress(GameClock.Instance?.Minute ?? 0) * 252);
        if (w > 0)
            DrawTextureRectRegion(_gameButt, new Rect2(3, 3, w, 37), new Rect2(0, 63, w, 37));
    }

    private static string T(int index) => OriginalTexts.Instance.Get(30, index);

    /// <summary>Per-unit-type icon tables (13 dwords each): go 0x4842E4, attack 0x484318, extort 0x48434C.</summary>
    private const uint GoIcons = 0x4842E4, AttackIcons = 0x484318, ExtortIcons = 0x48434C;

    private static int TypeIcon(uint table, UnitCommon.UnitType type) =>
        OriginalExe.Instance.IsLoaded ? (int)OriginalExe.Instance.ReadU32(table + (uint)type * 4) : 0;

    /// <summary>
    /// Unit menu (sub_42D2C1) for a selected own unit: Go (29,0), Attack (29,1), Extort Business (29,3) in slots 0–2.
    /// The button arms the order; the next left click on the map carries it out (Main.PendingOrder).
    /// TODO: Stand Ground (29,4), Patrol (29,8, slot 5), Force Go/Attack (29,6/7 with Shift), Frenzy (29,5), Unload
    /// (29,9, Cadillac), the per-type order gate unit vt+1C4h, the enemy-unit buttons (orders 0Bh/0Ch).
    /// </summary>
    private bool BuildUnitMenu(Unit unit)
    {
        var main = Main.Instance;
        if (main == null)
            return false;
        void Arm(Main.OrderMode mode) => main.PendingOrder = mode;
        Set(0, TypeIcon(GoIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 0), () => Arm(Main.OrderMode.Go));
        Set(1, TypeIcon(AttackIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 1), () => Arm(Main.OrderMode.Attack));
        if (unit.CanAttack != null && unit.UnitType is not (UnitCommon.UnitType.Cadillac or UnitCommon.UnitType.Collector))
            Set(2, TypeIcon(ExtortIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 3), () => Arm(Main.OrderMode.Extort));
        return true;
    }

    /// <summary>Rebuilds the slots when what they depend on changed (or always when <paramref name="force"/>).</summary>
    private void Rebuild(bool force)
    {
        if (GuiGameback.Instance?.SelectedNode is Unit selectedUnit && IsInstanceValid(selectedUnit)
            && selectedUnit.OwnerTeam == Teams.GetCurrentPlayerTeam() && !selectedUnit.IsDead
            && selectedUnit.UnitType is not UnitCommon.UnitType.Collector)
        {
            var unitKey = $"unit|{selectedUnit.GetInstanceId()}";
            if (!force && unitKey == _shown)
                return;
            _shown = unitKey;
            for (var i = 0; i < SlotCount; i++)
            {
                _slots[i].Visible = false;
                _actions[i] = null;
            }
            BuildUnitMenu(selectedUnit);
            return;
        }
        var company = GuiGameback.Instance?.SelectedNode as Company;
        var player = Teams.GetCurrentPlayerTeam();
        var own = company != null && IsInstanceValid(company) && company.IsEconomic && company.OwnerTeam == player && player != null;
        if (!own || company != _submenuCompany)
            _submenuCompany = null;
        var key = own
            ? $"{company.GetInstanceId()}|{company.Entry?.Index}|{company.Busy}|{company.Hp >= company.MaxHp}|{_submenuCompany != null}|{_submenuType}|{Training.PrimaryCenter(player) == company}|{(int)player.Org.Favour[4]}|{player.Org.AuthorityMoney[4]}"
            : "";
        if (!force && key == _shown)
            return;
        _shown = key;
        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i].IsHovered() && _slots[i].Visible)
                PanelInfo.StatusText = "";
            _slots[i].Visible = false;
            _actions[i] = null;
        }
        if (!own || company.Busy)
            return;

        if (_submenuCompany != null)
        {
            var offers = BusinessRules.Offers(company, _submenuType);
            for (var i = 0; i < offers.Count; i++)
            {
                var row = offers[i];
                var favour = BusinessRules.FavourNeeded(row);
                var price = PanelInfo.Money(row.Price);
                var text = favour < 1 ? PanelInfo.CFormat(T(9), row.Name, price)
                    : favour == 1 ? PanelInfo.CFormat(T(10), row.Name, price)
                    : PanelInfo.CFormat(T(11), row.Name, price, favour);
                Set(i, IconUpgrade, text, () =>
                {
                    company.StartConversion(row);
                    _submenuCompany = null;
                    Rebuild(true);
                });
            }
            Set(SlotCount - 1, IconBack, T(12), () =>
            {
                _submenuCompany = null;
                Rebuild(true);
            });
            return;
        }

        if (company.BusinessType == OriginalBusinessTable.BusinessType.Legal)
        {
            for (var i = 0; i < TypeButtons.Length; i++)
            {
                var (type, icon, text) = TypeButtons[i];
                Set(i, icon, T(text), () =>
                {
                    _submenuCompany = company;
                    _submenuType = type;
                    Rebuild(true);
                });
            }
            return;
        }

        // Command 12: "Set as primary training center" (30,8) for an own training-center business that isn't primary.
        if (Training.IsTrainingCenter(company) && Training.PrimaryCenter(player) != company)
            Set(4, IconTrainingCenter, T(8), () =>
            {
                player.Org.PrimaryCenter = company;
                Rebuild(true);
            });

        if (company.CanRepair)
            Set(SlotCount - 1, IconRepair, PanelInfo.CFormat(T(6), company.RepairCost), () =>
            {
                company.StartRepair();
                Rebuild(true);
            });
        else if (company.CanSell)
            Set(SlotCount - 1, IconSell, PanelInfo.CFormat(T(7), company.SellPrice), () =>
            {
                company.Sell();
                Rebuild(true);
            });
    }
}
