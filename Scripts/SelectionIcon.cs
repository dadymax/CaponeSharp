using Godot;

namespace Capone;

/// <summary>Avatar of the selected unit or company: a Chicago.tile portrait (see OriginalTiles.ResolveTile).</summary>
public partial class SelectionIcon : TextureRect
{
    public Node2D SelectedNode { get; private set; }

    /// <summary>Portraits are 84 × 95 (Chicago.tile "info" / unit faces).</summary>
    public static readonly Vector2 PortraitSize = new(84, 95);

    public override void _Ready()
    {
        Visible = false;
        // A fixed box: with the default "keep size" mode the control shrank to 0 × 0 when the texture was cleared
        // while hidden and stayed so, which left the portrait blank after reselecting.
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepCentered;
        Size = PortraitSize;
    }

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
            PlainBuilding plain => tiles.ResolveTile(plain.Portrait),
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
        Size = PortraitSize;
    }

    /// <summary>
    /// Chicago.tile "info …" portrait (BLOCKINF.BMP, 4 × 4 pictures of 84 × 95) of a building, picture vt+68h:
    /// <list type="bullet">
    /// <item>company (sub_422158): by the block's district (vt+16Ch) and the catalogue row's grade (+0Ch) — no block:
    /// extra 1; slum: slum; middle class: grade 3 theathern, 2 L-house, 1 news stand, else hall; center: grade 4
    /// hotel 2, 3 hotel 1, 2 central 1, else L-house. (A company released to an unowned legal row by sub_422D82 gets
    /// state 5 when the local player isn't the owner, and then shows hall / hotel 2; not modelled.)</item>
    /// <item>HQ (sub_415258): an owned HQ the "HQ info picture" (0D8Dh); police station (sub_429D30): police.</item>
    /// </list>
    /// </summary>
    public static string CompanyPortrait(Company company)
    {
        if (company.IsHq)
            return "HQ info picture";
        if (company.IsPoliceStation)
            return "info police";
        if (company.Block == null)
            return "info extra 1";
        var grade = company.Entry?.Grade ?? 0;
        return company.Block.District switch
        {
            0 => "info slum",
            1 => grade switch { 3 => "info theathern", 2 => "info L-house", 1 => "info news stand", _ => "info hall" },
            2 => grade switch { 4 => "info hotel 2", 3 => "info hotel 1", 2 => "info central 1", _ => "info L-house" },
            _ => "info extra 1",
        };
    }
}
