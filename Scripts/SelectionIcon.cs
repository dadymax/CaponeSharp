using Godot;

namespace Capone;

/// <summary>Avatar of the selected unit or company, from its AvatarTileIndex in Chicago.tile.</summary>
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

        var avatarIndex = node switch
        {
            Unit unit => unit.UnitData?.AvatarTileIndex ?? -1,
            Company company => company.CompanyData?.AvatarTileIndex ?? -1,
            _ => -1,
        };
        if (avatarIndex < 0)
        {
            GD.PrintErr("SelectionIcon: no AvatarTileIndex for ", node.Name);
            return;
        }

        var tile = OriginalData.Instance.Tiles.GetTileByIndex(avatarIndex);
        if (tile == null)
            return;
        var images = OriginalData.Instance.GetImageForTile(tile);
        if (images.Count == 0)
        {
            GD.PrintErr("SelectionIcon: no images for tile index: ", avatarIndex);
            return;
        }
        Texture = ImageTexture.CreateFromImage(images[0]);
    }
}
