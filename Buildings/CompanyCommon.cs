using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Per-type company data (companies.json), per-grade economics (companies2.json) and component nodes.</summary>
public partial class CompanyCommon : Node
{
    public enum CompanyType { Free, HQ, Occupied, Bootlegging, Entertainment, Gambling, Troubleshooting, SupportFire, SupportMoney, SupportTraining, NonInteractive }
    public enum CompanyOwner { Neutral, Player, Teammate, Enemy, Disabled }
    public enum CompanyBldngGrade { Base, Middle, MiddleCorner, MiddleOther, High, HighCorner, HighOther, HQ, Police, Road }
    public enum CompanySubType { Standalone, Corner, Other }

    private const string DictNameSprites = "Sprites";

    /// <summary>Owner → animation name in SpriteFrames.</summary>
    public static readonly Dictionary<CompanyOwner, StringName> SpriteFrameNamesDict = new();

    private static readonly Dictionary<CompanyType, Godot.Collections.Dictionary> TypeData = new();
    private static readonly Dictionary<CompanyBldngGrade, Godot.Collections.Dictionary> GradeData = new();

    static CompanyCommon()
    {
        foreach (var owner in Enum.GetValues<CompanyOwner>())
            SpriteFrameNamesDict[owner] = owner.ToString();

        var types = JsonUtil.LoadDictionary("res://Buildings/companies.json");
        foreach (var type in Enum.GetValues<CompanyType>())
            if (types.TryGetValue(type.ToString(), out var v))
                TypeData[type] = v.AsGodotDictionary();

        var grades = JsonUtil.LoadDictionary("res://Buildings/companies2.json");
        foreach (var grade in Enum.GetValues<CompanyBldngGrade>())
            if (grades.TryGetValue(grade.ToString(), out var v))
                GradeData[grade] = v.AsGodotDictionary();
    }

    private CompanyType _companyType;
    private CompanyBldngGrade _companyGrade;
    private CompanySubType _companySubType = CompanySubType.Other;
    private CompanyEconomics _companyEconomics;

    public CompanyType Type => _companyType;

    public CompanyBldngGrade CompanyGrade
    {
        get => _companyGrade;
        set
        {
            _companyGrade = value;
            _companyEconomics = null;
            if (!GradeData.TryGetValue(value, out var gradeRecord))
                return;
            _companyEconomics = new CompanyEconomics();
            foreach (var (keyVariant, v) in gradeRecord)
            {
                var key = keyVariant.AsString();
                switch (key)
                {
                    case "Names":
                        var names = v.AsGodotArray();
                        if (names.Count == 0)
                            break;
                        var name = names[(int)(GD.Randi() % (uint)names.Count)].AsString();
                        _nameOriginal ??= name;
                        DisplayName = name;
                        break;
                    case "AvatarTileIndex":
                        AvatarTileIndex = JsonUtil.ToInt(v);
                        break;
                    default:
                        _companyEconomics.ApplyJsonValue(key, v);
                        break;
                }
            }
            UpdateIncomeForSubtype();
        }
    }

    public CompanySubType PropCompanySubType
    {
        get => _companySubType;
        set
        {
            _companySubType = value;
            UpdateIncomeForSubtype();
        }
    }

    public bool IsInteractive { get; set; } = true;
    public int AvatarTileIndex { get; set; } = -1;

    private string _name;
    private string _nameOriginal;
    /// <summary>Display name of the company (Node.Name is the node name, so this is separate).</summary>
    public string DisplayName
    {
        get => string.IsNullOrEmpty(_name) ? _nameOriginal : _name;
        set => _name = value;
    }

    public CanAttack CanAttack { get; private set; }
    public CanBeAttacked CanBeAttacked { get; private set; }
    public CanHoldMoney CanHoldMoney { get; private set; }
    public CompanyEconomics CompanyEconomics => _companyEconomics;

    /// <summary>Owner → Chicago.tile comment of its sign animation.</summary>
    private Dictionary<CompanyOwner, string> _spriteFrameDict = new()
    {
        [CompanyOwner.Neutral] = "",
        [CompanyOwner.Player] = "",
        [CompanyOwner.Teammate] = "",
        [CompanyOwner.Enemy] = "",
        [CompanyOwner.Disabled] = "",
    };

    /// <summary>Needed by Godot; use the (type) constructor in code.</summary>
    public CompanyCommon() { }

    public CompanyCommon(CompanyType type)
    {
        _companyType = type;
        var usedParams = new HashSet<string>();
        if (TypeData.TryGetValue(type, out var typeRecord))
        {
            foreach (var (keyVariant, v) in typeRecord)
            {
                var key = keyVariant.AsString();
                usedParams.Add(key);
                switch (key)
                {
                    case CanAttack.JsonKey:
                        CanAttack = JsonUtil.FillFromJson(new CanAttack(), v);
                        break;
                    case CanBeAttacked.JsonKey:
                        CanBeAttacked = JsonUtil.FillFromJson(new CanBeAttacked(), v);
                        break;
                    case CanHoldMoney.JsonKey:
                        CanHoldMoney = JsonUtil.FillFromJson(new CanHoldMoney(), v);
                        break;
                    case DictNameSprites:
                        _spriteFrameDict = new Dictionary<CompanyOwner, string>();
                        foreach (var (ownerVariant, comment) in v.AsGodotDictionary())
                            if (Enum.TryParse<CompanyOwner>(ownerVariant.AsString(), out var owner))
                                _spriteFrameDict[owner] = comment.AsString();
                        break;
                    case "isInteractive":
                        IsInteractive = v.AsBool();
                        break;
                    case "AvatarTileIndex":
                        AvatarTileIndex = JsonUtil.ToInt(v);
                        break;
                    default:
                        GD.PushWarning($"CompanyCommon: unknown json key '{key}'");
                        break;
                }
            }
        }
        else
        {
            GD.PrintErr("CompanyCommon: no companies.json record for ", type);
        }

        if (!usedParams.Contains(CanBeAttacked.JsonKey))
            CanBeAttacked = new CanBeAttacked();
        if (!usedParams.Contains(CanHoldMoney.JsonKey))
            CanHoldMoney = new CanHoldMoney();

        if (CanAttack != null) AddChild(CanAttack);
        if (CanBeAttacked != null) AddChild(CanBeAttacked);
        if (CanHoldMoney != null) AddChild(CanHoldMoney);

        CompanyGrade = CompanyGrade;
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        foreach (var (owner, comment) in _spriteFrameDict)
        {
            var layerName = SpriteFrameNamesDict[owner];
            sf.RemoveAnimation(layerName);
            sf.AddAnimation(layerName);
            if (string.IsNullOrEmpty(comment))
                continue;
            var tile = OriginalData.Instance.Tiles.GetRecordWithComment(comment);
            if (tile == null)
            {
                GD.PrintErr("CompanyCommon: no tile with comment: ", comment);
                continue;
            }
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                sf.AddFrame(layerName, ImageTexture.CreateFromImage(image));
            sf.SetAnimationLoopMode(layerName, SpriteFrames.LoopMode.Linear);
        }
        var disabled = SpriteFrameNamesDict[CompanyOwner.Disabled];
        if (sf.HasAnimation(disabled))
            sf.SetAnimationLoopMode(disabled, SpriteFrames.LoopMode.None);
    }

    private void UpdateIncomeForSubtype()
    {
        if (_companyEconomics == null || !GradeData.TryGetValue(_companyGrade, out var gradeRecord))
            return;

        var baseIncome = gradeRecord.TryGetValue("Income", out var income) ? JsonUtil.ToInt(income) : 0;
        if (gradeRecord.TryGetValue("Subtypes", out var subtypes)
            && subtypes.AsGodotDictionary().TryGetValue(_companySubType.ToString(), out var subtypeIncome))
        {
            _companyEconomics.Income = JsonUtil.ToInt(subtypeIncome);
            return;
        }
        _companyEconomics.Income = baseIncome;
    }

    /// <summary>Standalone if at most one neighbour cell is used, Corner if 2+ diagonal neighbours, else Other.</summary>
    public static CompanySubType DetermineSubType(TileMapLayer tileMap, Vector2I coords)
    {
        if (tileMap.TileSet == null)
            return CompanySubType.Other;

        Vector2I[] neighbours =
        {
            new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
        };

        var occupied = 0;
        var corner = 0;
        foreach (var dir in neighbours)
        {
            if (tileMap.GetCellTileData(coords + dir) == null)
                continue;
            occupied++;
            if (dir.X != 0 && dir.Y != 0)
                corner++;
        }

        if (occupied <= 1)
            return CompanySubType.Standalone;
        return corner >= 2 ? CompanySubType.Corner : CompanySubType.Other;
    }
}
