using System.Linq;
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
    /// <summary>Company whose type submenu is open, and the type (original dword_48029C / word_4802A0).</summary>
    private Company _submenuCompany;
    private OriginalBusinessTable.BusinessType _submenuType;
    private string _shown = "";
    private double _refresh;

    public override void _Ready()
    {
        Instance = this;
        Position = Origin;
        Size = new Vector2(SlotSize * SlotCount, SlotSize);
        MouseFilter = MouseFilterEnum.Ignore;
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = i;
            var button = new TextureButton { Position = new Vector2(SlotSize * i, 0), Size = new Vector2(SlotSize, SlotSize), Visible = false };
            button.Pressed += () =>
            {
                if (!Main.InputBlocked)
                    _actions[slot]?.Invoke();
            };
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

    /// <summary>Animated like the EXE's buttons (<see cref="TileIcons"/>).</summary>
    private static Texture2D Icon(int tileIndex) => TileIcons.Get(tileIndex);

    public static ActionBar Instance { get; private set; }


    /// <summary>Key shortcut (sub_410EC3): presses slot <paramref name="slot"/> if it is shown. Returns whether it was.</summary>
    public bool Press(int slot)
    {
        if (slot < 0 || slot >= SlotCount || !_slots[slot].Visible || _actions[slot] == null)
            return false;
        _actions[slot].Invoke();
        return true;
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
    private const uint GoIcons = 0x4842E4, AttackIcons = 0x484318, ExtortIcons = 0x48434C,         ForceGoIcons = 0x4843E8, ForceAttackIcons = 0x48441C, PatrolIcons = 0x484484, DisguiseIcons = 0x4844EC, UnloadIcons = 0x4844B8;
    /// <summary>Order 0Bh icon tile (sub_42D2C1 pushes 0AD8h).</summary>
    private const int ArrestIcon = 0xAD8;

    private static int TypeIcon(uint table, UnitCommon.UnitType type) =>
        OriginalExe.Instance.IsLoaded ? (int)OriginalExe.Instance.ReadU32(table + (uint)type * 4) : 0;

    /// <summary>
    /// Unit menu (sub_42D2C1) for a selected own unit: Go (29,0), Attack (29,1), Extort Business (29,3) in slots 0–2.
    /// The button arms the order; the next left click on the map carries it out (Main.PendingOrder).
    /// TODO: Stand Ground (blank icon, gate), Disguise (order 0Ah, 29,10, Hit man), Force Go/Attack (29,6/7 with Shift), Frenzy (29,5), Unload
    /// (29,9, Cadillac), the per-type order gate unit vt+1C4h, the enemy-unit buttons (orders 0Bh/0Ch).
    /// </summary>
    private bool BuildUnitMenu(Unit unit)
    {
        var main = Main.Instance;
        if (main == null)
            return false;
        void Arm(Main.OrderMode mode) => main.PendingOrder = mode;
        if (unit.IsCar)
        {
            // Cadillac menu (sub_42EF07): Go in slot 0; Unload (action 9, 29,9, icon table 0x4844B8) in slot 1 while
            // someone rides in it.
            Set(0, TypeIcon(GoIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 0), () => Arm(Main.OrderMode.Go));
            if (unit.HasPassengers)
                Set(1, TypeIcon(UnloadIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 9), unit.Unload);
            return true;
        }
        // With Shift held the first two become Force Go To / Force Attack (actions 5 / 6, texts 29,6 / 29,7).
        var force = Input.IsKeyPressed(Key.Shift);
        Set(0, TypeIcon(force ? ForceGoIcons : GoIcons, unit.UnitType), OriginalTexts.Instance.Get(29, force ? 6 : 0),
            () => Arm(force ? Main.OrderMode.ForceGo : Main.OrderMode.Go));
        // Attack, Extort and Patrol need a weapon (order gate sub_42CAF0): not for the Thief or the Godfather.
        var armed = unit.HasWeapon && unit.CanAttack != null;
        if (armed)
            Set(1, TypeIcon(force ? ForceAttackIcons : AttackIcons, unit.UnitType), OriginalTexts.Instance.Get(29, force ? 7 : 1),
                () => Arm(force ? Main.OrderMode.ForceAttack : Main.OrderMode.Attack));
        // Extort also needs the owner's HQ flag +1D2h (gate sub_42CCE5): not for a player without a base.
        if (armed && unit.OwnerTeam?.Org?.MayExtort != false)
            Set(2, TypeIcon(ExtortIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 3), () => Arm(Main.OrderMode.Extort));
        // Slot 5: order 8 "Patrol" (29,8) between the unit's cell and the clicked cell (PatrolOrder).
        if (armed)
            Set(5, TypeIcon(PatrolIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 8), () => Arm(Main.OrderMode.Patrol));
        // Slot 4: order 0Ah "Disguise" (29,10), an immediate command (sub_42C0C0) gated by sub_42CCE5.
        if (unit.CanDisguise)
            Set(4, TypeIcon(DisguiseIcons, unit.UnitType), OriginalTexts.Instance.Get(29, 10), () => unit.Disguise());
        // Order 3 "Stand Ground" (29,4): its icon table 0x484380 holds tile 0x38 ("Button upgrade up", blank) for every
        // type, so the original most likely hides it via vt+1C4h(3); not shown until that gate is decoded.
        return true;
    }

    private void ClearSlots()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i].IsHovered() && _slots[i].Visible)
                PanelInfo.StatusText = "";
            _slots[i].Visible = false;
            _actions[i] = null;
        }
    }

    /// <summary>Rebuilds the slots when what they depend on changed (or always when <paramref name="force"/>).</summary>
    private void Rebuild(bool force)
    {
        if (GuiGameback.Instance?.SelectedNode is Unit selectedUnit && IsInstanceValid(selectedUnit)
            && selectedUnit.OwnerTeam == Teams.GetCurrentPlayerTeam() && !selectedUnit.IsDead
            && selectedUnit.UnitType is not UnitCommon.UnitType.Collector)
        {
            var unitKey = $"unit|{selectedUnit.GetInstanceId()}|{Input.IsKeyPressed(Key.Shift)}|{selectedUnit.CanDisguise}|{selectedUnit.HasPassengers}";
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
        if (GuiGameback.Instance?.SelectedNode is Unit otherUnit && IsInstanceValid(otherUnit) && !otherUnit.IsDead
            && otherUnit.OwnerTeam != Teams.GetCurrentPlayerTeam())
        {
            // Another player's (or nobody's) unit: order 0Bh "Ask cops to arrest gangster" in slot 0 (sub_42D2C1).
            var reportable = otherUnit.CanBeReportedBy(Teams.GetCurrentPlayerTeam());
            var buyable = otherUnit.CanBuySnitch(Teams.GetCurrentPlayerTeam());
            var otherKey = $"other|{otherUnit.GetInstanceId()}|{reportable}|{buyable}";
            if (!force && otherKey == _shown)
                return;
            _shown = otherKey;
            ClearSlots();
            if (reportable)
                Set(0, ArrestIcon, OriginalTexts.Instance.Get(29, 11),
                    () => otherUnit.ReportToPolice(Teams.GetCurrentPlayerTeam()));
            // Slot 1: order 0Ch "Pay snitch to switch sides ($200.000)" (29,12), icon from table 0x4844EC.
            if (buyable)
                Set(1, TypeIcon(DisguiseIcons, otherUnit.UnitType), OriginalTexts.Instance.Get(29, 12),
                    () => otherUnit.BuySnitch(Teams.GetCurrentPlayerTeam()));
            return;
        }
        var company = GuiGameback.Instance?.SelectedNode as Company;
        var player = Teams.GetCurrentPlayerTeam();
        if (company != null && IsInstanceValid(company) && company.IsHq && player?.Org != null)
        {
            // HQ menu (sub_4152F0): authority services on the own or another player's HQ.
            var actions = HqServices.Menu(player, company);
            var hqKey = $"hq|{company.GetInstanceId()}|{string.Join(",", actions.Select(a => a.Slot + a.Text))}";
            if (!force && hqKey == _shown)
                return;
            _shown = hqKey;
            ClearSlots();
            foreach (var action in actions)
                Set(action.Slot, action.Icon, action.Text, () => action.Run());
            return;
        }
        if (company != null && IsInstanceValid(company) && company.IsEconomic && company.OwnerTeam != player && player?.Org != null)
        {
            // Another player's (or nobody's) company (sub_421370): raid, demolish, brownout.
            var actions = CompanyServices.Menu(player, company);
            var otherKey = $"othercompany|{company.GetInstanceId()}|{string.Join(",", actions.Select(a => a.Slot + a.Text))}";
            if (!force && otherKey == _shown)
                return;
            _shown = otherKey;
            ClearSlots();
            foreach (var action in actions)
                Set(action.Slot, action.Icon, action.Text, () => action.Run());
            return;
        }
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
