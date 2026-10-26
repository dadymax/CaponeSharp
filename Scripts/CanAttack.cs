using Godot;

namespace Capone;

/// <summary>
/// Attack component. Lives under UnitCommon/CompanyCommon, so the map object is GetParent().GetParent().
/// </summary>
public partial class CanAttack : Node, IJsonConfigurable
{
    public const string JsonKey = "can_attack";

    [Export] public float AttackRate { get; set; } = 1;
    /// <summary>Damage range against units.</summary>
    [Export] public int DamageMin { get; set; } = 1;
    [Export] public int DamageMax { get; set; } = 1;
    /// <summary>Damage range against buildings (companies).</summary>
    [Export] public int BuildingDamageMin { get; set; } = 1;
    [Export] public int BuildingDamageMax { get; set; } = 1;
    /// <summary>Attack range in tiles (melee units have 1.5 so diagonal neighbours count).</summary>
    [Export] public float AttackRange { get; set; } = 1;
    /// <summary>Unit price, used for the company capture chance.</summary>
    [Export] public int UnitCost { get; set; }
    [Export] public bool CanCaptureNeutral { get; set; } = true;
    /// <summary>Chance to miss (0..1). From the original's hit chance: miss = 1 - hit%.</summary>
    [Export] public float MissChance { get; set; } = 0.02f;

    /// <summary>Buildings can only be attacked from a neighbouring tile (diagonal included), whatever the unit's range.</summary>
    public const float BuildingAttackRange = 1.5f;

    private Node2D _targetParent;

    /// <summary>
    /// Health component of the current target. It is looked up from the target object each time,
    /// because a company replaces (and frees) its components when it changes hands.
    /// </summary>
    public CanBeAttacked Target
    {
        get
        {
            var component = (TargetParent as IGameEntity)?.CanBeAttacked;
            return component != null && IsInstanceValid(component) ? component : null;
        }
        set
        {
            _targetParent = value != null && IsInstanceValid(value) ? value.GetParent()?.GetParent() as Node2D : null;
            if (_targetParent != null && !IsHostile(_targetParent))
                _targetParent = null;
        }
    }

    /// <summary>The attacked map object (unit or company), or null if there is none or it was freed.</summary>
    public Node2D TargetParent => _targetParent != null && IsInstanceValid(_targetParent) ? _targetParent : null;

    /// <summary>Enemies and neutrals can be attacked; self and friends can't.</summary>
    private bool IsHostile(Node2D target)
    {
        var targetColor = (target as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;
        if (targetColor == _thisTeamColor)
            return false;
        return Teams.GetOrAddTeam(_thisTeamColor).GetTargetRelation(targetColor) != Teams.TeamRelation.Friend;
    }

    private Node2D _thisParent;
    private Color _thisTeamColor = Teams.NeutralTeamColor;
    private float _currentTimer;

    private SniperAbility _sniperAbility;
    private RaidAbility _raidAbility;
    private HitmanStealth _hitmanStealth;
    private BombAbility _bombAbility;
    private ThompsonAbility _thompsonAbility;

    public void ApplyJsonValue(string key, Variant value)
    {
        switch (key)
        {
            case "attackRate": AttackRate = JsonUtil.ToFloat(value); break;
            case "attackDamage":
                DamageMin = DamageMax = BuildingDamageMin = BuildingDamageMax = JsonUtil.ToInt(value);
                break;
            case "attackRange": AttackRange = JsonUtil.ToFloat(value); break;
            case "unitCost": UnitCost = JsonUtil.ToInt(value); break;
            case "can_capture_neutral": CanCaptureNeutral = value.AsBool(); break;
            case "missChance": MissChance = JsonUtil.ToFloat(value); break;
            default: GD.PushWarning($"CanAttack: unknown json key '{key}'"); break;
        }
    }

    /// <summary>Takes damage, range and hit chance from the Capone.EXE unit table.</summary>
    public void ApplyOriginalStats(OriginalUnitTable.Stats stats)
    {
        DamageMin = stats.DamageMin;
        DamageMax = Mathf.Max(stats.DamageMin, stats.DamageMax);
        BuildingDamageMin = stats.BuildingDamageMin;
        BuildingDamageMax = Mathf.Max(stats.BuildingDamageMin, stats.BuildingDamageMax);
        AttackRange = stats.AttackRange;
        MissChance = Mathf.Clamp(1f - stats.HitChance / 100f, 0f, 1f);
    }

    /// <summary>
    /// Random damage for the current target. Like Unit::GetMinDamage/GetMaxDamage (0x42C890/0x42C960):
    /// unit damage only against units other than the Cadillac; buildings and Cadillacs take building damage.
    /// TODO: the Hit man's fixed 20 damage after a time check (see TODO.md).
    /// </summary>
    public int RollDamage()
    {
        var againstUnit = TargetParent is Unit { UnitType: not UnitCommon.UnitType.Cadillac };
        var (min, max) = againstUnit ? (DamageMin, DamageMax) : (BuildingDamageMin, BuildingDamageMax);
        return max <= min ? min : GD.RandRange(min, max);
    }

    public void DecreaseAttackTimer(double delta)
    {
        if (_currentTimer > 0)
            _currentTimer = Mathf.Max(0, _currentTimer - (float)delta);
        if (_currentTimer == 0)
            AttackTarget();
    }

    public void ResetAttackTimer() => _currentTimer = AttackRate; // TODO: need good coefficient

    public UnitCommon.UnitType GetUnitType() =>
        _thisParent is Unit unit ? unit.UnitType : UnitCommon.UnitType.Thug;

    /// <summary>Hitman: is this unit visible to the target's team?</summary>
    public bool CheckStealth(Color targetColor) => _hitmanStealth?.IsVisibleToEnemy(targetColor) ?? true;

    /// <summary>Attack the current target, applying the unit type's special ability.</summary>
    public void AttackTarget()
    {
        if (Target == null)
            return;
        // The target may have changed hands since it was chosen (e.g. a company this unit just captured).
        if (!IsHostile(TargetParent))
        {
            _targetParent = null;
            return;
        }

        var unitType = GetUnitType();
        var targetColor = (TargetParent as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;

        if (unitType == UnitCommon.UnitType.Hitman && !CheckStealth(targetColor))
        {
            ResetAttackTimer();
            return;
        }

        if (unitType == UnitCommon.UnitType.Terrorist && _bombAbility != null && !_bombAbility.CanAttack())
        {
            ResetAttackTimer();
            return;
        }

        if (MissChance > 0 && GD.Randf() < MissChance)
        {
            ResetAttackTimer();
            return;
        }

        if (!TargetInRange())
            return;

        var damage = RollDamage();
        switch (unitType)
        {
            case UnitCommon.UnitType.Sniper:
                // The original sniper does flat table damage (200–250) up to its 9-tile range.
                Target.ReceiveDamage(damage, this);
                break;
            case UnitCommon.UnitType.Shotgun:
                _raidAbility.ApplyAreaDamage(damage);
                break;
            case UnitCommon.UnitType.Terrorist:
                _bombAbility.ApplySuicideDamage(Target, damage);
                break;
            case UnitCommon.UnitType.Thompson:
                _thompsonAbility.ApplyPiercingDamage(Target, damage);
                break;
            case UnitCommon.UnitType.Hitman:
                _hitmanStealth?.OnAttackInitiated();
                Target?.ReceiveDamage(damage, this);
                break;
            default:
                Target.ReceiveDamage(damage, this);
                break;
        }

        ResetAttackTimer();
    }

    /// <summary>
    /// One hit on a map object (unit vt+104h, used by the extortion order on every refusal). Returns the damage done
    /// (0 on a miss). TODO: the original Unit::Attack (sub_42BD15) also handles bursts, ammo and the Cadillac.
    /// </summary>
    public int HitOnce(Node2D target)
    {
        if (target is not IGameEntity { CanBeAttacked: { } health } || !IsInstanceValid(health))
            return 0;
        if (MissChance > 0 && GD.Randf() < MissChance)
            return 0;
        var againstUnit = target is Unit { UnitType: not UnitCommon.UnitType.Cadillac };
        var (min, max) = againstUnit ? (DamageMin, DamageMax) : (BuildingDamageMin, BuildingDamageMax);
        var damage = max <= min ? min : GD.RandRange(min, max);
        health.ReceiveDamage(damage, this);
        return damage;
    }

    public bool TargetInRange()
    {
        var target = TargetParent;
        if (_thisParent == null || target == null)
            return false;

        // Ranged attacks only work against units; a company has to be attacked from next to it.
        var range = target is Company ? BuildingAttackRange : AttackRange;
        return MapTileMath.GetDistanceExact(_thisParent, target) <= range;
    }

    public override void _Ready() => Reinit();

    public void Reinit()
    {
        _thisParent = GetParent()?.GetParent() as Node2D;
        _thisTeamColor = (_thisParent as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;

        switch (GetUnitType())
        {
            case UnitCommon.UnitType.Sniper when _sniperAbility == null:
                _sniperAbility = new SniperAbility();
                AddChild(_sniperAbility);
                break;
            case UnitCommon.UnitType.Shotgun when _raidAbility == null:
                _raidAbility = new RaidAbility();
                AddChild(_raidAbility);
                break;
            case UnitCommon.UnitType.Hitman when _hitmanStealth == null:
                _hitmanStealth = new HitmanStealth();
                AddChild(_hitmanStealth);
                break;
            case UnitCommon.UnitType.Terrorist when _bombAbility == null:
                _bombAbility = new BombAbility();
                AddChild(_bombAbility);
                break;
            case UnitCommon.UnitType.Thompson when _thompsonAbility == null:
                _thompsonAbility = new ThompsonAbility();
                AddChild(_thompsonAbility);
                break;
        }
    }
}
