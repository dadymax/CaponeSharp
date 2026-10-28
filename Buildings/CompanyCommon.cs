using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Visual and health part of a company: the sign sprites per sign type and owner (companies.json, Chicago.tile names)
/// and the <see cref="CanBeAttacked"/> component. The economy lives in <see cref="Company"/>.
/// </summary>
public partial class CompanyCommon : Node
{
    /// <summary>Sign type (which sign the building shows). Keys of companies.json.</summary>
    public enum CompanyType { Free, HQ, Occupied, Bootlegging, Entertainment, Gambling, Troubleshooting, SupportFire, SupportMoney, SupportTraining, NonInteractive }
    /// <summary>Owner relative to the local player; selects the sign animation.</summary>
    public enum CompanyOwner { Neutral, Player, Teammate, Enemy, Disabled }

    private const string DictNameSprites = "Sprites";

    /// <summary>Owner → animation name in SpriteFrames.</summary>
    public static readonly Dictionary<CompanyOwner, StringName> SpriteFrameNamesDict = new();

    private static readonly Dictionary<CompanyType, Godot.Collections.Dictionary> TypeData = new();

    static CompanyCommon()
    {
        foreach (var owner in Enum.GetValues<CompanyOwner>())
            SpriteFrameNamesDict[owner] = owner.ToString();
        var types = JsonUtil.LoadDictionary("res://Buildings/companies.json");
        foreach (var type in Enum.GetValues<CompanyType>())
            if (types.TryGetValue(type.ToString(), out var v))
                TypeData[type] = v.AsGodotDictionary();
    }

    public CompanyType Type { get; private set; }
    public bool IsInteractive { get; private set; } = true;
    public CanBeAttacked CanBeAttacked { get; private set; }

    /// <summary>Owner → Chicago.tile reference of the sign animation.</summary>
    private Dictionary<CompanyOwner, string> _spriteFrameDict = new();

    /// <summary>Needed by Godot; use the (type) constructor in code.</summary>
    public CompanyCommon() { }

    public CompanyCommon(CompanyType type)
    {
        CanBeAttacked = new CanBeAttacked();
        AddChild(CanBeAttacked);
        SetSignType(type);
    }

    public void SetMaxHp(int maxHp)
    {
        CanBeAttacked.MaxHP = maxHp;
        CanBeAttacked.HP = maxHp;
    }

    public void SetSignType(CompanyType type)
    {
        Type = type;
        IsInteractive = true;
        _spriteFrameDict = new Dictionary<CompanyOwner, string>();
        if (!TypeData.TryGetValue(type, out var record))
        {
            GD.PrintErr("CompanyCommon: no companies.json record for ", type);
            return;
        }
        foreach (var (keyVariant, v) in record)
        {
            switch (keyVariant.AsString())
            {
                case DictNameSprites:
                    foreach (var (ownerVariant, reference) in v.AsGodotDictionary())
                        if (Enum.TryParse<CompanyOwner>(ownerVariant.AsString(), out var owner))
                            _spriteFrameDict[owner] = reference.AsString();
                    break;
                case "isInteractive":
                    IsInteractive = v.AsBool();
                    break;
            }
        }
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        foreach (var owner in Enum.GetValues<CompanyOwner>())
        {
            var layerName = SpriteFrameNamesDict[owner];
            if (sf.HasAnimation(layerName))
                sf.RemoveAnimation(layerName);
            sf.AddAnimation(layerName);
            if (!_spriteFrameDict.TryGetValue(owner, out var reference) || string.IsNullOrEmpty(reference))
                continue;
            var tile = OriginalData.Instance.Tiles.ResolveTile(reference);
            if (tile == null)
            {
                GD.PrintErr("CompanyCommon: no tile for: ", reference);
                continue;
            }
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                sf.AddFrame(layerName, ImageTexture.CreateFromImage(image));
            sf.SetAnimationLoopMode(layerName,
                owner == CompanyOwner.Disabled ? SpriteFrames.LoopMode.None : SpriteFrames.LoopMode.Linear);
        }
    }
}
