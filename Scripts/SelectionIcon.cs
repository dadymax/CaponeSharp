using Godot;

namespace Capone;

/// <summary>Avatar of the selected unit or company: a Chicago.tile portrait (see OriginalTiles.ResolveTile).</summary>
public partial class SelectionIcon : TextureRect
{
    public Node2D SelectedNode { get; private set; }

    public override void _Ready() => Visible = false;

    public void UpdateIcon(Node2D node)
    {
        SelectedNode = node;
        Visible = node != null;
        Texture = null; // force reload
        if (node == null)
            return;

        var tiles = OriginalData.Instance.Tiles;
        var tile = node switch
        {
            Unit unit => tiles.ResolveTile(unit.UnitData?.Avatar),
            Company { CompanyData.AvatarTileIndex: >= 0 } company => tiles.GetTileByIndex(company.CompanyData.AvatarTileIndex),
            _ => null,
        };
        if (tile == null)
        {
            GD.PrintErr("SelectionIcon: no avatar for ", node.Name);
            return;
        }
        var images = OriginalData.Instance.GetImageForTile(tile);
        if (images.Count == 0)
        {
            GD.PrintErr("SelectionIcon: avatar tile has no frames for ", node.Name);
            return;
        }
        Texture = ImageTexture.CreateFromImage(images[0]);
    }
}
