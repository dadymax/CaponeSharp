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
    /// <summary>Panel position of slot 0 (screen 232, 505).</summary>
    public static readonly Vector2 Origin = new(232, 505 - PanelInfo.PanelTop);

    private const int IconUpgrade = 0xAD4, IconRepair = 0xAD6, IconTroubleshooting = 0xADA, IconBootlegging = 0xADC,
        IconBack = 0xADE, IconGambling = 0xAE0, IconEntertainment = 0xAE2, IconSupport = 0xAE4, IconSell = 0xD47;

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
        button.TooltipText = tooltip;
        button.Visible = true;
        _actions[slot] = action;
    }

    private static string T(int index) => OriginalTexts.Instance.Get(30, index);

    /// <summary>Rebuilds the slots when what they depend on changed (or always when <paramref name="force"/>).</summary>
    private void Rebuild(bool force)
    {
        var company = GuiGameback.Instance?.SelectedNode as Company;
        var player = Teams.GetCurrentPlayerTeam();
        var own = company != null && IsInstanceValid(company) && company.IsEconomic && company.OwnerTeam == player && player != null;
        if (!own || company != _submenuCompany)
            _submenuCompany = null;
        var key = own
            ? $"{company.GetInstanceId()}|{company.Entry?.Index}|{company.UnderConstruction}|{company.Hp >= company.MaxHp}|{_submenuCompany != null}|{_submenuType}|{(int)player.Org.Favour[4]}|{player.Org.AuthorityMoney[4]}"
            : "";
        if (!force && key == _shown)
            return;
        _shown = key;
        for (var i = 0; i < SlotCount; i++)
        {
            _slots[i].Visible = false;
            _actions[i] = null;
        }
        if (!own || company.UnderConstruction)
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

        if (company.CanSell)
            Set(SlotCount - 1, IconSell, PanelInfo.CFormat(T(7), company.SellPrice), () =>
            {
                company.Sell();
                Rebuild(true);
            });
    }
}
