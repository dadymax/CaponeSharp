using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Per-type unit data plus the unit's component nodes.
/// Stats come from Capone.EXE (OriginalUnitTable); units.json adds sprites, avatar and the values not found in the EXE yet.
/// </summary>
public partial class UnitCommon : Node
{
    /// <summary>Original unit type numbers: index into the EXE unit table and the type field of .epi UNIT lines.</summary>
    public enum UnitType
    {
        Thug = 0,
        Pistol = 1,
        Thompson = 2,
        Terrorist = 3,
        Sniper = 4,
        Cadillac = 5,
        /// <summary>Not confirmed: 600 HP, fastest unit; Chicago.tile has "Police car" sprites.</summary>
        PoliceCar = 6,
        Collector = 7,
        Shotgun = 8,
        Hitman = 9,
        Thief = 10,
        /// <summary>Not confirmed: the EXE names a "Sergeant"; Chicago.tile has "Police Batman".</summary>
        Policeman = 11,
        /// <summary>Named bosses in the episodes (Аль Капоне, Джонни Торрио…).</summary>
        Godfather = 12,
    }

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
            var record = new TypeRecord();
            TypeRecords[type] = record;
            if (!json.TryGetValue(type.ToString(), out var typeVariant))
            {
                GD.PrintErr("UnitCommon: no units.json record for ", type);
                continue;
            }
            foreach (var (keyVariant, value) in typeVariant.AsGodotDictionary())
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

    /// <summary>Owner → array of tile references (one per animation layer), or owner → name of another owner to copy.</summary>
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

    /// <summary>Unit type for an original type number, or null if it is not a known type.</summary>
    public static UnitType? FromOriginalId(int id) =>
        Enum.IsDefined(typeof(UnitType), id) ? (UnitType)id : null;

    /// <summary>Sprite references of every type and owner that don't resolve in Chicago.tile.</summary>
    public static IEnumerable<string> ValidateSprites()
    {
        foreach (var (type, record) in TypeRecords)
        {
            foreach (var (owner, sprites) in record.Sprites)
                foreach (var missing in sprites.Validate())
                    yield return $"{type}/{owner}: {missing}";
            if (record.Properties.TryGetValue("Avatar", out var avatar)
                && OriginalData.Instance.Tiles.ResolveTile(avatar.AsString()) == null)
                yield return $"{type}/Avatar: {avatar.AsString()}";
        }
    }

    public UnitType Type { get; private set; }
    public UnitOwner OwnerType { get; set; }

    /// <summary>Chicago.tile reference of the portrait shown in the bottom panel.</summary>
    public string Avatar { get; set; } = "";
    public string UnitName { get; set; } = "";
    public int Cost { get; set; }
    public int ArmyFavor { get; set; }
    public int Salary { get; set; } = 20000;
    /// <summary>Pixels per second.</summary>
    public float MoveSpeed { get; set; } = 40;
    public int VisionRadius { get; set; } = 10;
    /// <summary>Largest vision radius (the sniper's after standing still).</summary>
    public int VisionRadiusMax { get; set; } = 10;
    public OriginalUnitTable.Stats Stats { get; private set; }

    public CanAttack CanAttack { get; private set; }
    public CanBeAttacked CanBeAttacked { get; private set; }
    public CanHoldMoney CanHoldMoney { get; private set; }

    private readonly Dictionary<UnitOwner, OriginalUnitSprites> _spriteFrameDict = new();

    /// <summary>Needed by Godot; use the (type) constructor in code.</summary>
    public UnitCommon() { }

    public UnitCommon(UnitType type)
    {
        Type = type;
        var record = TypeRecords.GetValueOrDefault(type) ?? new TypeRecord();
        Stats = OriginalUnitTable.Instance.Get((int)type);
        if (Stats == null)
            GD.PrintErr("UnitCommon: no Capone.EXE stats for ", type);

        ApplyOriginalStats();
        foreach (var (key, value) in record.Properties)
            ApplyProperty(key, value);
        foreach (var (owner, sprites) in record.Sprites)
            _spriteFrameDict[owner] = sprites;

        // Units without damage in the original table don't attack.
        var attacks = Stats == null ? record.Components.ContainsKey(CanAttack.JsonKey) : Stats.DamageMax > 0;
        if (attacks)
        {
            CanAttack = record.Components.TryGetValue(CanAttack.JsonKey, out var ca)
                ? JsonUtil.FillFromJson(new CanAttack(), ca)
                : new CanAttack();
            if (CanAttack != null && Stats != null)
                CanAttack.ApplyOriginalStats(Stats);
        }

        CanBeAttacked = record.Components.TryGetValue(CanBeAttacked.JsonKey, out var cba)
            ? JsonUtil.FillFromJson(new CanBeAttacked(), cba)
            : new CanBeAttacked();
        if (CanBeAttacked != null && Stats != null)
        {
            CanBeAttacked.MaxHP = Stats.MaxHP;
            // The original has no evasion: the attacker's hit chance decides.
            CanBeAttacked.EvasionFactor = 0;
        }

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

    private void ApplyOriginalStats()
    {
        UnitName = OriginalUnitTable.GetName((int)Type, OriginalData.TextLanguage);
        if (Stats == null)
            return;
        Cost = Stats.Cost;
        VisionRadius = Stats.Vision;
        VisionRadiusMax = Mathf.Max(Stats.Vision, Stats.VisionMax);
        if (Stats.TicksPerTile > 0)
            MoveSpeed = (float)(MapTileMath.TileStepLength * GameClock.MinutesPerRealSecond / Stats.TicksPerTile); // TicksPerTile = game minutes per tile
    }

    private void ApplyProperty(string key, Variant value)
    {
        switch (key)
        {
            case "ArmyFavor": ArmyFavor = JsonUtil.ToInt(value); break;
            case "Salary": Salary = JsonUtil.ToInt(value); break;
            case "Avatar": Avatar = value.AsString(); break;
            default: GD.PushWarning($"UnitCommon: unknown json key '{key}'"); break;
        }
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        if (_spriteFrameDict.TryGetValue(OwnerType, out var ous))
            ous.FillSpriteFrames(sf);
    }
}
