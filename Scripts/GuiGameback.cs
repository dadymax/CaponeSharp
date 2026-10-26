using Godot;

namespace Capone;

/// <summary>Bottom game panel (GAMEBACK.BMP) with the selected object's avatar.</summary>
public partial class GuiGameback : TextureRect
{
    public static GuiGameback Instance { get; private set; }

    private SelectionIcon _selectionIcon;

    /// <summary>The object whose avatar and info texts the panel shows.</summary>
    public Node2D SelectedNode => _selectionIcon?.SelectedNode;

    public override void _Ready()
    {
        Instance = this;
        _selectionIcon = new SelectionIcon();

        if (!OriginalData.Instance.Bmps.Bmps.TryGetValue("GAMEBACK.BMP", out var bmp))
        {
            GD.PrintErr("GuiGameback: GAMEBACK.BMP not found in Bmps!");
            _selectionIcon.Visible = false;
            AddChild(_selectionIcon);
            return;
        }

        var image = bmp.Body;
        if (image == null)
        {
            GD.PrintErr("GuiGameback: GAMEBACK.BMP image is null!");
            return;
        }

        Texture = ImageTexture.CreateFromImage(image);
        ExpandMode = ExpandModeEnum.FitWidth;
        SizeFlagsHorizontal = SizeFlags.Fill;
        SizeFlagsVertical = SizeFlags.Fill;

        // Over the black avatar rectangle in GAMEBACK.BMP.
        _selectionIcon.Position = new Vector2(232, 12);
        AddChild(_selectionIcon);
        AddChild(new PanelInfo { Name = "PanelInfo" });
        AddChild(new ActionBar { Name = "ActionBar" });
        AddChild(new RecruitPanel { Name = "RecruitPanel" });
        AddChild(new SharesPanel { Name = "SharesPanel" });
    }

    public static void UpdateSelectionIcon(Node2D node)
    {
        if (Instance?._selectionIcon == null)
        {
            GD.PrintErr("GuiGameback: not ready, cannot update the selection icon");
            return;
        }
        Instance._selectionIcon.UpdateIcon(node);
    }
}
