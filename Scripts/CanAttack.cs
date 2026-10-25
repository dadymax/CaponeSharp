using Godot;

namespace Capone;

/// <summary>
/// Attack component. Lives under UnitCommon/CompanyCommon, so the map object is GetParent().GetParent().
/// </summary>
public partial class CanAttack : Node, IJsonConfigurable
{
    public const string JsonKey = "can_attack";

    [Export] public float AttackRate { get; set; } = 1;
    [Export] public int AttackDamage { get; set; } = 1;
    [Export] public int AttackRange { get; set; } = 1;
    /// <summary>Unit price, used for the company capture chance.</summary>
    [Export] public int UnitCost { get; set; }
    [Export] public bool CanCaptureNeutral { get; set; } = true;
    /// <summary>Fixed miss chance (2 % by default).</summary>
    [Export] public float MissChance { get; set; } = 0.02f;

    private CanBeAttacked _target;
    public CanBeAttacked Target
    {
        get => _target;
        set
        {
            _target = value;
            if (_target == null)
            {
                TargetParent = null;
                return;
            }

            TargetParent = _target.GetParent()?.GetParent() as Node2D;
            var targetColor = (TargetParent as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;
            if (targetColor == _thisTeamColor)
            {
                Target = null;
                return;
            }
            var relation = Teams.GetOrAddTeam(_thisTeamColor).GetTargetRelation(targetColor);
            if (relation == Teams.TeamRelation.Friend)
                Target = null;
            // Neutral companies stay targetable: attacking them captures them.
        }
    }

    public Node2D TargetParent { get; private set; }

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
            case "attackDamage": AttackDamage = JsonUtil.ToInt(value); break;
            case "attackRange": AttackRange = JsonUtil.ToInt(value); break;
            case "unitCost": UnitCost = JsonUtil.ToInt(value); break;
            case "can_capture_neutral": CanCaptureNeutral = value.AsBool(); break;
            case "missChance": MissChance = JsonUtil.ToFloat(value); break;
            default: GD.PushWarning($"CanAttack: unknown json key '{key}'"); break;
        }
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
        _thisParent is Unit unit ? unit.UnitType : UnitCommon.UnitType.Bat;

    /// <summary>Hitman: is this unit visible to the target's team?</summary>
    public bool CheckStealth(Color targetColor) => _hitmanStealth?.IsVisibleToEnemy(targetColor) ?? true;

    /// <summary>Attack the current target, applying the unit type's special ability.</summary>
    public void AttackTarget()
    {
        if (Target == null)
            return;

        var unitType = GetUnitType();
        var targetColor = (TargetParent as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;

        if (unitType == UnitCommon.UnitType.Hitman && !CheckStealth(targetColor))
        {
            ResetAttackTimer();
            return;
        }

        if (unitType == UnitCommon.UnitType.Bomb && _bombAbility != null && !_bombAbility.CanAttack())
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

        // Attacking a neutral company captures it instead of damaging it.
        if (CanCaptureNeutral && TargetParent is Company targetCompany
            && targetCompany.CompanyOwner == CompanyCommon.CompanyOwner.Neutral)
        {
            targetCompany.CaptureBy(_thisTeamColor, _thisParent);
            ResetAttackTimer();
            return;
        }

        switch (unitType)
        {
            case UnitCommon.UnitType.Sniper:
                _sniperAbility.UpdateAttackRange();
                Target.ReceiveDamage((int)(AttackDamage * _sniperAbility.GetDamageMultiplier()), this);
                break;
            case UnitCommon.UnitType.Raid:
                _raidAbility.ApplyAreaDamage(AttackDamage);
                break;
            case UnitCommon.UnitType.Bomb:
                _bombAbility.ApplySuicideDamage(Target, AttackDamage);
                break;
            case UnitCommon.UnitType.Thompson:
                _thompsonAbility.ApplyPiercingDamage(Target, AttackDamage);
                break;
            case UnitCommon.UnitType.Hitman:
                _hitmanStealth?.OnAttackInitiated();
                Target?.ReceiveDamage(AttackDamage, this);
                break;
            default:
                Target.ReceiveDamage(AttackDamage, this);
                break;
        }

        ResetAttackTimer();
    }

    public bool TargetInRange()
    {
        if (_thisParent == null || TargetParent == null)
            return false;

        // Sniper: attack range equals vision.
        if (GetUnitType() == UnitCommon.UnitType.Sniper && _sniperAbility != null)
            return _sniperAbility.UpdateAttackRange() > 0;

        return MapTileMath.GetDistance(_thisParent, TargetParent) <= AttackRange;
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
            case UnitCommon.UnitType.Raid when _raidAbility == null:
                _raidAbility = new RaidAbility();
                AddChild(_raidAbility);
                break;
            case UnitCommon.UnitType.Hitman when _hitmanStealth == null:
                _hitmanStealth = new HitmanStealth();
                AddChild(_hitmanStealth);
                break;
            case UnitCommon.UnitType.Bomb when _bombAbility == null:
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
