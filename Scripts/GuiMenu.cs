using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Top menu: GAMEBUTT.BMP button that opens the Menu.bmp drop-down.</summary>
public partial class GuiMenu : Control
{
    private const int MenuCols = 4;
    private const int MenuRows = 2;

    public static GuiMenu Instance { get; private set; }

    public bool IsMenuOpen { get; private set; }

    // Menu.bmp order: alliances, messages, statistics, money, tips, replay, exit (8th cell = nothing)
    private static readonly string[] MenuItems = { "alliances", "messages", "statistics", "money", "tips", "reroll", "exit" };

    private TextureButton _menuButton;
    private Panel _menuPanel;
    private TextureRect _menuBackground;
    private readonly List<TextureRect> _menuHighlights = new();
    private readonly List<Texture2D> _menuItemTextures = new();
    private readonly List<Texture2D> _menuItemPressedTextures = new();
    private Texture2D _menuButtonNormal;
    private Texture2D _menuButtonPressed;

    public override void _Ready()
    {
        Instance = this;

        // GAMEBUTT.BMP: top half = normal button, bottom half = pressed.
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("GAMEBUTT.BMP", out var bmp) && bmp.Body is { } image)
        {
            var width = image.GetWidth();
            var halfHeight = image.GetHeight() / 2;
            _menuButtonNormal = ImageTexture.CreateFromImage(image.GetRegion(new Rect2I(0, 0, width, halfHeight)));
            _menuButtonPressed = ImageTexture.CreateFromImage(image.GetRegion(new Rect2I(0, halfHeight, width, halfHeight)));
        }

        CreateMenuButton();
        CreateMenuPanel();
        LoadMenuTextures();
        CreateMenuItems();

        Position = new Vector2(10, 10);
        Size = new Vector2(200, 50);
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void CreateMenuButton()
    {
        _menuButton = new TextureButton
        {
            TextureNormal = _menuButtonNormal,
            TexturePressed = _menuButtonPressed,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _menuButton.Pressed += OnMenuButtonPressed;
        AddChild(_menuButton);
    }

    private void CreateMenuPanel()
    {
        _menuPanel = new Panel
        {
            Visible = false,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        AddChild(_menuPanel);

        _menuBackground = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _menuPanel.AddChild(_menuBackground);
    }

    /// <summary>Menu.bmp is a 4x2 grid of equally sized pictures.</summary>
    private void LoadMenuTextures()
    {
        if (!OriginalData.Instance.Bmps.Bmps.TryGetValue("MENU.BMP", out var bmp) || bmp.Body is not { } image)
            return;

        var cellWidth = image.GetWidth() / MenuCols;
        var cellHeight = image.GetHeight() / MenuRows;
        for (var row = 0; row < MenuRows; row++)
            for (var col = 0; col < MenuCols; col++)
            {
                var cell = image.GetRegion(new Rect2I(col * cellWidth, row * cellHeight, cellWidth, cellHeight));
                var texture = ImageTexture.CreateFromImage(cell);
                if (row == 0)
                    _menuItemTextures.Add(texture);
                else
                    _menuItemPressedTextures.Add(texture);
            }
    }

    private void CreateMenuItems()
    {
        if (_menuItemTextures.Count == 0)
        {
            GD.PrintErr("GuiMenu: no menu textures loaded");
            return;
        }

        var bgSize = _menuBackground.Size;
        if (bgSize.X <= 0 || bgSize.Y <= 0)
            bgSize = new Vector2(400, 200); // not laid out yet: use an estimate

        var cellWidth = bgSize.X / MenuCols;
        var cellHeight = bgSize.Y / MenuRows;

        var count = Mathf.Min(MenuItems.Length, _menuItemTextures.Count);
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var highlight = new TextureRect
            {
                Texture = _menuItemTextures[i],
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Size = new Vector2(cellWidth, cellHeight),
                Position = new Vector2(Mathf.Floor(i % MenuCols * cellWidth), Mathf.Floor(i / MenuCols * cellHeight)),
                MouseFilter = MouseFilterEnum.Pass,
                Visible = false,
            };
            highlight.MouseEntered += () => HighlightItem(index);
            highlight.MouseExited += () => HighlightItem(index);
            highlight.GuiInput += ev => OnItemGuiInput(ev, index);
            _menuHighlights.Add(highlight);
            _menuPanel.AddChild(highlight);
        }
    }

    private void OnMenuButtonPressed()
    {
        IsMenuOpen = !IsMenuOpen;
        _menuPanel.Visible = IsMenuOpen;
        if (!IsMenuOpen)
            return;
        var buttonRect = GetGlobalRect();
        _menuPanel.Position = new Vector2(buttonRect.Position.X, buttonRect.Position.Y + buttonRect.Size.Y);
        foreach (var highlight in _menuHighlights)
            highlight.Visible = false;
    }

    public void HighlightItem(int index)
    {
        for (var i = 0; i < _menuHighlights.Count; i++)
            _menuHighlights[i].Visible = i == index;
    }

    private void OnItemGuiInput(InputEvent ev, int index)
    {
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            OnMenuItemSelected(index);
            CloseMenu();
        }
    }

    private void OnMenuItemSelected(int index)
    {
        if (index >= MenuItems.Length)
            return;
        switch (MenuItems[index])
        {
            case "alliances": GD.Print("GuiMenu: Alliances — stub"); break;
            case "messages": GD.Print("GuiMenu: Messages — stub"); break;
            case "statistics": GD.Print("GuiMenu: Statistics — stub"); break;
            case "money": GD.Print("GuiMenu: Money details — stub"); break;
            case "tips": GD.Print("GuiMenu: Tips — stub"); break;
            case "reroll": GD.Print("GuiMenu: Reroll — stub"); break;
            case "exit":
                GD.Print("GuiMenu: Exit");
                GetTree().Quit();
                break;
        }
    }

    public void CloseMenu()
    {
        IsMenuOpen = false;
        _menuPanel.Visible = false;
        foreach (var highlight in _menuHighlights)
            highlight.Visible = false;
    }
}
