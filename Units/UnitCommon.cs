using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Per-type unit data from units.json plus the unit's component nodes.</summary>
public partial class UnitCommon : Node
{
    /// <summary>Must stay in sync with the keys of units.json.</summary>
    public enum UnitType { Bat, Pistol, Sniper, Raid, Hitman, Thief, Bomb, Thompson, Collector, CollectorWithMoney, Cadillac }
    public enum UnitOwner { Neutral, Player, Teammate, Enemy }

    private const string DictNameSprites = "Sprites";

    private class TypeRecord
    {
        public readonly Dictionary<string, Variant> Properties = new();
        public readonly Dictionary<string, Variant> Components = new();
        public readonly Dictionary<UnitOwner, OriginalUnitSprites> Sprites = new();
    }

    private static readonly Dictionary<UnitType, TypeRecord> TypeRecords = new();

    static UnitCommon()
    {
        var json = JsonUtil.LoadDictionary("res://Units/units.json");
        foreach (var type in Enum.GetValues<UnitType>())
        {
            if (!json.TryGetValue(type.ToString(), out var typeVariant))
                continue; // e.g. CollectorWithMoney has no record
            var typeDict = typeVariant.AsGodotDictionary();
            var record = new TypeRecord();
            TypeRecords[type] = record;

            foreach (var (keyVariant, value) in typeDict)
            {
                var key = keyVariant.AsString();
                switch (key)
                {
                    case CanAttack.JsonKey:
                    case CanBeAttacked.JsonKey:
                    case CanHoldMoney.JsonKey:
                        record.Components[key] = value;
                        break;
                    case DictNameSprites:
                        ReadSprites(value.AsGodotDictionary(), record.Sprites);
                        break;
                    default:
                        if (value.VariantType is Variant.Type.Int or Variant.Type.Float or Variant.Type.Bool or Variant.Type.String)
                            record.Properties[key] = value;
                        break;
                }
            }
        }
    }

    /// <summary>Owner → array of tile comments (one per animation layer), or owner → name of another owner to copy.</summary>
    private static void ReadSprites(Godot.Collections.Dictionary sprites, Dictionary<UnitOwner, OriginalUnitSprites> result)
    {
        var aliases = new List<(UnitOwner owner, UnitOwner source)>();
        foreach (var (ownerVariant, value) in sprites)
        {
            if (!Enum.TryParse<UnitOwner>(ownerVariant.AsString(), out var owner))
                continue;
            if (value.VariantType == Variant.Type.Array)
            {
                var layers = value.AsGodotArray();
                var ous = new OriginalUnitSprites();
                var count = Math.Min(OriginalUnitSprites.SpriteLayers.Count, layers.Count);
                for (var i = 0; i < count; i++)
                    ous.SearchLineDict[OriginalUnitSprites.SpriteLayers[i]] = layers[i].AsString();
                result[owner] = ous;
            }
            else if (value.VariantType == Variant.Type.String
                     && Enum.TryParse<UnitOwner>(value.AsString(), out var source))
            {
                aliases.Add((owner, source));
            }
        }
        foreach (var (owner, source) in aliases)
            if (result.TryGetValue(source, out var ous))
                result[owner] = ous;
    }

    public UnitType Type { get; private set; }
    public UnitOwner OwnerType { get; set; }

    public int AvatarTileIndex { get; set; } = -1;
    public string UnitName { get; set; } = "";
    public int Cost { get; set; }
    public int ArmyFavor { get; set; }
    public int Salary { get; set; } = 20000;
    public int MoveSpeed { get; set; } = 40;
    public int VisionRadius { get; set; } = 10;

    public CanAttack CanAttack { get; private set; }
    public CanBeAttacked CanBeAttacked { get; private set; }
    public CanHoldMoney CanHoldMoney { get; private set; }

    private readonly Dictionary<UnitOwner, OriginalUnitSprites> _spriteFrameDict = new();

    /// <summary>Needed by Godot; use the (type) constructor in code.</summary>
    public UnitCommon() { }

    public UnitCommon(UnitType type)
    {
        Type = type;
        if (!TypeRecords.TryGetValue(type, out var record))
        {
            GD.PrintErr("UnitCommon: no units.json record for ", type);
            record = new TypeRecord();
        }

        foreach (var (key, value) in record.Properties)
            ApplyProperty(key, value);
        foreach (var (owner, sprites) in record.Sprites)
            _spriteFrameDict[owner] = sprites;

        if (record.Components.TryGetValue(CanAttack.JsonKey, out var ca))
            CanAttack = JsonUtil.FillFromJson(new CanAttack(), ca);
        else
            CanAttack = new CanAttack(); // default, as in the GDScript version
        if (record.Components.TryGetValue(CanBeAttacked.JsonKey, out var cba))
            CanBeAttacked = JsonUtil.FillFromJson(new CanBeAttacked(), cba);
        else
            CanBeAttacked = new CanBeAttacked();
        if (record.Components.TryGetValue(CanHoldMoney.JsonKey, out var chm))
            CanHoldMoney = JsonUtil.FillFromJson(new CanHoldMoney(), chm);

        if (CanAttack != null)
        {
            CanAttack.UnitCost = Cost;
            AddChild(CanAttack);
        }
        if (CanBeAttacked != null)
            AddChild(CanBeAttacked);
        if (CanHoldMoney != null)
            AddChild(CanHoldMoney);
    }

    private void ApplyProperty(string key, Variant value)
    {
        switch (key)
        {
            case "Name": UnitName = value.AsString(); break;
            case "Cost": Cost = JsonUtil.ToInt(value); break;
            case "ArmyFavor": ArmyFavor = JsonUtil.ToInt(value); break;
            case "Salary": Salary = JsonUtil.ToInt(value); break;
            case "MoveSpeed": MoveSpeed = JsonUtil.ToInt(value); break;
            case "AvatarTileIndex": AvatarTileIndex = JsonUtil.ToInt(value); break;
            case "visionRadius": VisionRadius = JsonUtil.ToInt(value); break;
            default: GD.PushWarning($"UnitCommon: unknown json key '{key}'"); break;
        }
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        if (_spriteFrameDict.TryGetValue(OwnerType, out var ous))
            ous.FillSpriteFrames(sf);
    }
}
