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
            Company company => tiles.ResolveTile(CompanyPortrait(company)),
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

    /// <summary>
    /// Chicago.tile "info …" portrait (84×95) of a building. TODO: the original picks it per building; until that
    /// mapping is found: grade 1 slum house, grade 2 house, grade 3 central building.
    /// </summary>
    public static string CompanyPortrait(Company company) => company switch
    {
        { IsHq: true } => "HQ info picture",
        { IsPoliceStation: true } => "info police",
        { Entry.Grade: 3 } => "info central 1",
        { Entry.Grade: 2 } => "info house 1",
        _ => "info slum",
    };
}
