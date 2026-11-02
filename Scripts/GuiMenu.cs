using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Top menu. The MENU button comes from GAMEBUTT.BMP, the drop-down from Menu.bmp.
/// <para>GAMEBUTT.BMP (258×100) holds several panel graphics; the button is the pair of "Меню" images
/// side by side at y = 43: normal on the left, pressed on the right (sub_40C103).</para>
/// <para>Menu.bmp (368×216) is a 4×2 grid of complete menus: cell 0 has nothing highlighted, cell i
/// highlights item i (1 = Альянсы … 7 = ВЫХОД). Hovering an item shows its cell; the item zones are the
/// rows where cell i differs from cell 0.</para>
/// </summary>
public partial class GuiMenu : Control
{
    // sub_40C103: GameButt.bmp (158, 43)–(208, 56) normal, (208, 43)–(258, 56) pressed, drawn at (5, 0)
    // (dword_476BE8/BEC); the click area is (5, 0)–(55, 13), so the message lines (y 13) start right under it.
    private static readonly Rect2I ButtonNormalRect = new(158, 43, 50, 13);
    private static readonly Rect2I ButtonPressedRect = new(208, 43, 50, 13);
    private const int MenuCols = 4;
    private const int MenuRows = 2;

    public static GuiMenu Instance { get; private set; }

    public bool IsMenuOpen { get; private set; }

    /// <summary>Menu items top to bottom (Menu.bmp cells 1..7).</summary>
    private static readonly string[] MenuItems = { "alliances", "messages", "statistics", "money", "tips", "reroll", "exit" };

    private TextureButton _menuButton;
    private TextureRect _menuView;
    /// <summary>Menu.bmp cells: [0] nothing highlighted, [i] item i highlighted.</summary>
    private readonly List<Texture2D> _menuStates = new();
    /// <summary>Vertical zone (first row, last row) of each item inside a cell.</summary>
    private readonly List<Vector2I> _itemBands = new();
    private int _hoverItem = -1;

    public override void _Ready()
    {
        Instance = this;
        Position = new Vector2(5, 0);
        // The root itself never takes clicks; only the button and the open menu do.
        MouseFilter = MouseFilterEnum.Ignore;

        CreateMenuButton();
        LoadMenuStates();
        CreateMenuView();
    }

    private void CreateMenuButton()
    {
        _menuButton = new TextureButton { ToggleMode = true, MouseFilter = MouseFilterEnum.Stop };
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("GAMEBUTT.BMP", out var bmp) && bmp.Body is { } image)
        {
            _menuButton.TextureNormal = ImageTexture.CreateFromImage(image.GetRegion(ButtonNormalRect));
            _menuButton.TexturePressed = ImageTexture.CreateFromImage(image.GetRegion(ButtonPressedRect));
        }
        else
        {
            GD.PrintErr("GuiMenu: GAMEBUTT.BMP not found");
        }
        _menuButton.Toggled += pressed =>
        {
            if (pressed)
                OpenMenu();
            else
                CloseMenu();
        };
        AddChild(_menuButton);
    }

    private void LoadMenuStates()
    {
        if (!OriginalData.Instance.Bmps.Bmps.TryGetValue("MENU.BMP", out var bmp) || bmp.Body is not { } image)
        {
            GD.PrintErr("GuiMenu: MENU.BMP not found");
            return;
        }

        var cellWidth = image.GetWidth() / MenuCols;
        var cellHeight = image.GetHeight() / MenuRows;
        var cells = new List<Image>();
        for (var row = 0; row < MenuRows; row++)
            for (var col = 0; col < MenuCols; col++)
                cells.Add(image.GetRegion(new Rect2I(col * cellWidth, row * cellHeight, cellWidth, cellHeight)));
        foreach (var cell in cells)
            _menuStates.Add(ImageTexture.CreateFromImage(cell));

        for (var item = 1; item <= MenuItems.Length && item < cells.Count; item++)
            _itemBands.Add(FindChangedRows(cells[0], cells[item]));
        // Items without a detectable highlight fall back to an even split.
        for (var i = 0; i < _itemBands.Count; i++)
            if (_itemBands[i].X < 0)
            {
                var h = cellHeight / MenuItems.Length;
                _itemBands[i] = new Vector2I(i * h, (i + 1) * h - 1);
            }
    }

    /// <summary>First and last row where the two images differ, or (-1, -1).</summary>
    private static Vector2I FindChangedRows(Image a, Image b)
    {
        int first = -1, last = -1;
        for (var y = 0; y < a.GetHeight(); y++)
            for (var x = 0; x < a.GetWidth(); x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    if (first < 0)
                        first = y;
                    last = y;
                    break;
                }
        return new Vector2I(first, last);
    }

    private void CreateMenuView()
    {
        _menuView = new TextureRect
        {
            Visible = false,
            Texture = _menuStates.Count > 0 ? _menuStates[0] : null,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _menuView.GuiInput += OnMenuGuiInput;
        _menuView.MouseExited += () => SetHover(-1);
        AddChild(_menuView);
    }

    private void OpenMenu()
    {
        IsMenuOpen = true;
        _menuView.Position = new Vector2(0, ButtonNormalRect.Size.Y);
        SetHover(-1);
        _menuView.Visible = true;
    }

    public void CloseMenu()
    {
        IsMenuOpen = false;
        _menuView.Visible = false;
        _menuButton.SetPressedNoSignal(false);
    }

    private int ItemAt(float y)
    {
        for (var i = 0; i < _itemBands.Count; i++)
            if (y >= _itemBands[i].X && y <= _itemBands[i].Y)
                return i;
        return -1;
    }

    private void SetHover(int item)
    {
        _hoverItem = item;
        var state = item + 1;
        if (state >= 0 && state < _menuStates.Count)
            _menuView.Texture = _menuStates[state];
    }

    private void OnMenuGuiInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion motion:
                var item = ItemAt(motion.Position.Y);
                if (item != _hoverItem)
                    SetHover(item);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                var selected = ItemAt(click.Position.Y);
                CloseMenu();
                if (selected >= 0)
                    OnMenuItemSelected(selected);
                AcceptEvent();
                break;
        }
    }

    private void OnMenuItemSelected(int index)
    {
        switch (MenuItems[index])
        {
            case "alliances": GD.Print("GuiMenu: Alliances — stub"); break;
            case "messages": GD.Print("GuiMenu: Messages — stub"); break;
            case "statistics": GD.Print("GuiMenu: Statistics — stub"); break;
            case "money": GD.Print("GuiMenu: Money details — stub"); break;
            case "tips":
                // sub_414760(1): the episode's TIPTEXT in the info window; a second call closes it.
                if (GetTree().Root.FindChild("InfoWindow", true, false) is InfoWindow open)
                    open.Close();
                else if (CampaignManager.Instance?.CurrentScenario is { } scenario)
                    Main.Instance?.ShowInfoWindow(OriginalTexts.Instance.Get(5, 85), scenario.TipText, null);
                break;
            case "reroll": GD.Print("GuiMenu: Reroll — stub"); break;
            case "exit":
                // Asks first (info window mode 2); OK leaves the episode for the main menu.
                Main.Instance?.ConfirmLeave();
                break;
        }
    }
}
