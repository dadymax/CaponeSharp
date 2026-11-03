using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Attack component (lives under UnitCommon, so the map object is GetParent().GetParent()), ported from the EXE:
/// <list type="bullet">
/// <item>A unit thinks once a game hour (event 5, sub_42A535) and then attacks its target if it is in range
/// (Unit::Attack sub_42BD15, also vt+104h for the extortion hits): the aim counter grows by one; a shot needs
/// <see cref="AimTicksNeeded"/> of them (table +00h) and <see cref="ReloadMinutes"/> since the last shot (+2Eh). A
/// Sniper re-aims at a new target; any unit hit loses its aim (vt+0ECh sub_42BC00). From inside a car only the
/// Thompson man shoots (+30h). A Terrorist dies with his blast.</item>
/// <item>The shot (sub_438873): round(rate) bullets (at least one); each covers the cells within table +26h − 1 of
/// the target. On each cell holding an object (a unit, or its car, or a building): with the stray chance (+28h) the
/// bullet goes to a random neighbouring cell instead; the object there (not the shooter) is hit when random(100) ≤
/// the hit chance (+0Ch; × 0.2 from inside a car) for min + random(max − min) damage against that object (unit
/// damage, or building damage for buildings and Cadillacs). Area shots hurt anyone, friends included.</item>
/// <item>Hit points change without any evasion roll (sub_438710).</item>
/// </list>
/// </summary>
public partial class CanAttack : Node, IJsonConfigurable
{
    public const string JsonKey = "can_attack";

    /// <summary>Game hours between shots at the best (max of aim and reload); for display and tests.</summary>
    [Export] public float AttackRate { get; set; } = 1;
    /// <summary>Damage range against units.</summary>
    [Export] public int DamageMin { get; set; } = 1;
    [Export] public int DamageMax { get; set; } = 1;
    /// <summary>Damage range against buildings (companies) and Cadillacs.</summary>
    [Export] public int BuildingDamageMin { get; set; } = 1;
    [Export] public int BuildingDamageMax { get; set; } = 1;
    /// <summary>Attack range in tiles (melee units have 1.5 so diagonal neighbours count).</summary>
    [Export] public float AttackRange { get; set; } = 1;
    /// <summary>Unit price, used for the company capture chance.</summary>
    [Export] public int UnitCost { get; set; }

    /// <summary>Table +0Ch: chance (percent) that a bullet hits.</summary>
    public int HitChance { get; set; } = 100;
    /// <summary>Table +00h rounded: bullets per shot (Thompson 6).</summary>
    public int Bullets { get; set; } = 1;
    /// <summary>Table +26h − 1: cells around the target a bullet covers (Shotgun, Terrorist 1).</summary>
    public int AreaRadius { get; set; }
    /// <summary>Table +28h: chance (percent) that a bullet strays to a neighbouring cell (Thompson 50).</summary>
    public int StrayChance { get; set; }
    /// <summary>vt+1B4h: thinks of aiming per shot.</summary>
    public int AimTicksNeeded { get; set; } = 1;
    /// <summary>vt+1B8h: game minutes between shots.</summary>
    public int ReloadMinutes { get; set; }
    /// <summary>vt+1BCh: may shoot from inside a Cadillac (Thompson man).</summary>
    public bool FiresFromCar { get; set; }

    /// <summary>Hit chance factor from inside a car (flt_4846A4).</summary>
    public const float CarHitFactor = 0.2f;

    /// <summary>Buildings can only be attacked from a neighbouring tile (diagonal included), whatever the unit's range.</summary>
    public const float BuildingAttackRange = 1.5f;

    private Node2D _targetParent;
    private Node2D _thisParent;
    private Color _thisTeamColor = Teams.NeutralTeamColor;
    /// <summary>+0C8h: aim counter.</summary>
    private int _aim;
    /// <summary>+0D0h: game minute of the last shot.</summary>
    private long _lastShot = long.MinValue / 2;
    /// <summary>Game hours until the next think (one game hour = one timer second).</summary>
    private double _thinkTimer = GD.Randf();

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
            var previous = _targetParent;
            _targetParent = value != null && IsInstanceValid(value) ? value.GetParent()?.GetParent() as Node2D : null;
            if (_targetParent != null && !IsHostile(_targetParent))
                _targetParent = null;
            // sub_42BD15: a Sniper starts aiming again at a new target (+0B8h, aim counter +0C8h = 0).
            if (_targetParent != null && _targetParent != previous && GetUnitType() == UnitCommon.UnitType.Sniper)
                _aim = 0;
        }
    }

    /// <summary>The attacked map object (unit or company), or null if there is none or it was freed.</summary>
    public Node2D TargetParent => _targetParent != null && IsInstanceValid(_targetParent) ? _targetParent : null;

    /// <summary>The target of a Force Attack order: it may be the player's own or an ally's unit or building.</summary>
    public Node2D ForcedTarget { get; set; }

    /// <summary>Enemies and neutrals can be attacked; self and friends can't, unless named by a Force Attack.</summary>
    private bool IsHostile(Node2D target)
    {
        if (target != _thisParent && target == ForcedTarget && IsInstanceValid(target))
            return true;
        if (target != _thisParent && ((_thisParent as Unit)?.IsRogue == true || (target as Unit)?.IsRogue == true))
            return true;
        var targetColor = (target as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;
        if (targetColor == _thisTeamColor)
            return false;
        return Teams.GetOrAddTeam(_thisTeamColor).GetTargetRelation(targetColor) != Teams.TeamRelation.Friend;
    }

    public void ApplyJsonValue(string key, Variant value)
    {
        switch (key)
        {
            case "attackDamage":
                DamageMin = DamageMax = BuildingDamageMin = BuildingDamageMax = JsonUtil.ToInt(value);
                break;
            case "attackRange": AttackRange = JsonUtil.ToFloat(value); break;
            case "unitCost": UnitCost = JsonUtil.ToInt(value); break;
            case "attackRate" or "missChance" or "can_capture_neutral": break; // the EXE table decides
            default: GD.PushWarning($"CanAttack: unknown json key '{key}'"); break;
        }
    }

    /// <summary>Damage, range, hit chance, bullets, area, stray and timing from the Capone.EXE unit table.</summary>
    public void ApplyOriginalStats(OriginalUnitTable.Stats stats)
    {
        DamageMin = stats.DamageMin;
        DamageMax = Mathf.Max(stats.DamageMin, stats.DamageMax);
        BuildingDamageMin = stats.BuildingDamageMin;
        BuildingDamageMax = Mathf.Max(stats.BuildingDamageMin, stats.BuildingDamageMax);
        AttackRange = stats.AttackRange;
        HitChance = stats.HitChance;
        Bullets = Mathf.Max(1, (int)System.Math.Round(stats.FireRate));
        AreaRadius = Mathf.Max(0, stats.AttackKind - 1);
        StrayChance = stats.Value28;
        AimTicksNeeded = AimTicks(stats.FireRate);
        ReloadMinutes = stats.ReloadMinutes;
        FiresFromCar = stats.FiresFromCar != 0;
        AttackRate = Mathf.Max(AimTicksNeeded, ReloadMinutes / (float)GameClock.MinutesPerHour);
    }

    /// <summary>sub_42BBB0 (vt+1B4h): thinks of aiming per shot — 1 at a rate of 1 or more, else round(1 / rate).</summary>
    public static int AimTicks(float fireRate) =>
        fireRate >= 1f || fireRate <= 0f ? 1 : (int)System.Math.Round(1.0 / fireRate);

    /// <summary>Shot sound per unit type (dword_484554 → sound table 0x478700).</summary>
    public static string ShotSound(UnitCommon.UnitType type) => type switch
    {
        UnitCommon.UnitType.Pistol => "Gun.wav",
        UnitCommon.UnitType.Thompson or UnitCommon.UnitType.PoliceCar => "Machgun.wav",
        UnitCommon.UnitType.Terrorist => "Bang.wav",
        UnitCommon.UnitType.Sniper => "Rifle.wav",
        UnitCommon.UnitType.Cadillac => "CarGo.wav",
        UnitCommon.UnitType.Shotgun => "Shotgun.wav",
        UnitCommon.UnitType.Hitman or UnitCommon.UnitType.Policeman => "Hitman.wav",
        _ => "Hit.wav",
    };

    /// <summary>
    /// Unit::GetMinDamage / GetMaxDamage (0x42C890 / 0x42C960) against <paramref name="target"/>: unit damage against
    /// units other than the Cadillac, building damage otherwise; an exposed Hit man does 20–30 to units (14h / 1Eh).
    /// </summary>
    public (int Min, int Max) DamageRange(Node2D target)
    {
        var againstUnit = target is Unit { UnitType: not UnitCommon.UnitType.Cadillac };
        if (againstUnit && _thisParent is Unit { UnitType: UnitCommon.UnitType.Hitman } hitman && hitman.IsExposed)
            return (Unit.ExposedHitmanDamageMin, Unit.ExposedHitmanDamageMax);
        return againstUnit ? (DamageMin, DamageMax) : (BuildingDamageMin, BuildingDamageMax);
    }

    /// <summary>sub_438873: min + random(max − min).</summary>
    public int RollDamage(Node2D target)
    {
        var (min, max) = DamageRange(target);
        return max > min ? min + (int)(GD.Randi() % (uint)(max - min)) : min;
    }

    public int RollDamage() => RollDamage(TargetParent);

    /// <summary>Called every frame with game-scaled time: one think (and attack step) per game hour.</summary>
    public void DecreaseAttackTimer(double delta)
    {
        _thinkTimer -= delta;
        while (_thinkTimer <= 0)
        {
            _thinkTimer += 1;
            AttackTarget();
        }
    }

    /// <summary>Starts aiming from the beginning.</summary>
    public void ResetAttackTimer() => _aim = 0;

    /// <summary>vt+0ECh sub_42BC00: a unit that is hit loses its aim.</summary>
    public void OnHit() => _aim = 0;

    public UnitCommon.UnitType GetUnitType() =>
        _thisParent is Unit unit ? unit.UnitType : UnitCommon.UnitType.Thug;

    /// <summary>One attack step on the current target (Think → Unit::Attack).</summary>
    public void AttackTarget()
    {
        var target = TargetParent;
        if (target == null)
            return;
        // The target may have changed hands since it was chosen (e.g. a company this unit just captured).
        if (!IsHostile(target))
        {
            _targetParent = null;
            return;
        }
        if (!TargetInRange())
            return;
        Attack(target);
    }

    /// <summary>
    /// Unit vt+104h (the extortion order on every refusal): one attack step on <paramref name="target"/>. Returns the
    /// damage done this step (0 while aiming / reloading or on a miss).
    /// </summary>
    public int HitOnce(Node2D target) => target is IGameEntity { CanBeAttacked: not null } ? Attack(target) : 0;

    /// <summary>sub_42BD15: aim, then shoot when ready. Returns the damage done to <paramref name="target"/>.</summary>
    private int Attack(Node2D target)
    {
        var self = _thisParent as Unit;
        if (self is { Vehicle: not null } && !FiresFromCar)
            return 0;
        if (self is { HasWeapon: false })
            return 0;
        _aim++;
        var now = GameClock.Instance?.Minute ?? 0;
        if (_aim < AimTicksNeeded || now - _lastShot < ReloadMinutes)
            return 0;
        _aim = 0;
        _lastShot = now;
        // The shot's sound when the shooter or the target can be seen (vt+0DCh, read as visible to the local player).
        if ((_thisParent?.Visible ?? false) || target.Visible)
            Sound.Play(ShotSound(GetUnitType()));
        var done = Fire(target);
        // sub_42BD15 stamps the last shot (+0D0h) after the bullets (sub_438873): a hidden Hit man's shot still does
        // his table damage, and only then is he exposed.
        self?.MarkShot();
        if (GetUnitType() == UnitCommon.UnitType.Terrorist && self != null && !self.IsDead)
            self.Die(); // sub_42BD15: HP 0 and Killed(null) — the blast takes him too
        return done;
    }

    /// <summary>sub_438873: the bullets of one shot. Returns the damage done to <paramref name="target"/>.</summary>
    private int Fire(Node2D target)
    {
        var map = MapTileMath.Tilemap;
        if (map == null || _thisParent == null)
            return 0;
        var self = _thisParent as Unit;
        var myCell = map.LocalToMap(_thisParent.Position);
        var center = map.LocalToMap(target.Position);
        var hitChance = self is { Vehicle: not null } ? (int)System.Math.Round(HitChance * CarHitFactor) : HitChance;
        var done = 0;
        for (var b = 0; b < Bullets; b++)
            for (var dx = -AreaRadius; dx <= AreaRadius; dx++)
                for (var dy = -AreaRadius; dy <= AreaRadius; dy++)
                {
                    var cell = center + new Vector2I(dx, dy);
                    // On the target's own cell the bullet meets the target (the original keeps one object per cell;
                    // here units may share cells).
                    var victim = dx == 0 && dy == 0 ? target : ObjectAt(cell);
                    if (victim == null)
                        continue;
                    if ((int)(GD.Randi() % 100) < StrayChance)
                        victim = Stray(cell, myCell);
                    if (victim == null || victim == _thisParent)
                        continue;
                    if ((int)(GD.Randi() % 100) > hitChance)
                        continue;
                    if ((victim as IGameEntity)?.CanBeAttacked is not { } health || !IsInstanceValid(health))
                        continue;
                    var damage = RollDamage(victim);
                    health.ReceiveDamage(damage, this);
                    if (victim == target)
                        done += damage;
                }
        return done;
    }

    /// <summary>A stray bullet: a random neighbouring cell (of the cell aimed at) with an object, not the shooter's.</summary>
    private static Node2D Stray(Vector2I cell, Vector2I myCell)
    {
        for (var tries = 0; tries < 30; tries++)
        {
            var next = cell + new Vector2I((int)(GD.Randi() % 3) - 1, (int)(GD.Randi() % 3) - 1);
            if (next == myCell)
                continue;
            var victim = ObjectAt(next);
            if (victim != null)
                return victim;
        }
        return null;
    }

    /// <summary>The map object on <paramref name="cell"/> (sub_43BFB0 / sub_43AB00): a unit on the map, else a building.</summary>
    public static Node2D ObjectAt(Vector2I cell)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return null;
        var unit = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .FirstOrDefault(u => IsInstanceValid(u) && !u.IsDead && !u.IsArrested && !u.InsideBuilding && u.Cell == cell);
        if (unit != null)
            return unit;
        return tree.GetNodesInGroup(Company.GroupName).OfType<Company>()
            .FirstOrDefault(c => IsInstanceValid(c) && c.Cell == cell && c.CanBeAttacked != null);
    }

    public bool TargetInRange()
    {
        var target = TargetParent;
        if (_thisParent == null || target == null)
            return false;

        // Ranged attacks only work against units; a company has to be attacked from next to it.
        if (target is Company)
            return MapTileMath.GetDistanceExact(_thisParent, target) <= BuildingAttackRange;
        // sub_42B750 (unit vt+118h): the target must be in the attacker's current vision square and seen, then within
        // the table range (+14h).
        if (_thisParent is Unit self && !self.Sees(target))
            return false;
        return MapTileMath.GetDistanceExact(_thisParent, target) <= AttackRange;
    }

    public override void _Ready() => Reinit();

    public void Reinit()
    {
        _thisParent = GetParent()?.GetParent() as Node2D;
        _thisTeamColor = (_thisParent as IGameEntity)?.TeamColor ?? Teams.NeutralTeamColor;
    }
}
