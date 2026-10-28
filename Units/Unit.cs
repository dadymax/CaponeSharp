using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>A unit on the map: movement over an A* grid, animation, and its UnitCommon data/components.</summary>
public partial class Unit : CharacterBody2D, ISelectable, IGameEntity
{
    public const string GroupName = "units";
    /// <summary>Corpses stay on the map this many game minutes.</summary>
    private const int CorpseMinutes = 3 * GameClock.MinutesPerHour;

    private Selection _selection;
    public bool Selectable => !IsDead && !IsArrested;
    public bool Selected
    {
        get => _selection?.Enabled ?? false;
        set
        {
            if (_selection != null)
                _selection.Enabled = value;
        }
    }

    public Color TeamColor { get; set; } = Teams.NeutralTeamColor;
    /// <summary>Owning player, or null for neutral units (police, units that resigned).</summary>
    public Team OwnerTeam => Teams.GetTeam(TeamColor);
    public TileMapLayer Map { get; private set; }

    private AStarGrid2D _astarNav;
    private CollisionShape2D _shape;
    private AnimatedSprite2D _as2d;

    private UnitCommon _unitInstance;
    private UnitCommon.UnitOwner _unitOwner;

    public UnitCommon UnitData => _unitInstance;

    /// <summary>Personal name from the episode file (e.g. "Аль Капоне"); empty for ordinary units.</summary>
    public string PersonalName { get; set; } = "";
    /// <summary>Name to show: the personal name, or the unit type's name from Capone.EXE.</summary>
    public string DisplayName => string.IsNullOrEmpty(PersonalName) ? _unitInstance?.UnitName ?? "" : PersonalName;
    /// <summary>Raw 5th and 6th numbers of the .epi UNIT line (see Scenario.EpiStartingUnit).</summary>
    public int EpiFlagA { get; set; }
    public int EpiFlagB { get; set; }
    public UnitCommon.UnitType UnitType { get; private set; }
    public UnitCommon.UnitOwner UnitOwner => _unitOwner;

    public CanAttack CanAttack => _unitInstance?.CanAttack;
    public CanBeAttacked CanBeAttacked => _unitInstance?.CanBeAttacked;
    public CanHoldMoney CanHoldMoney => _unitInstance?.CanHoldMoney;

    // ---- state used by the economy ------------------------------------------------------------------------------------

    /// <summary>Weekly salary (unit table +0x20), paid daily as salary / 7 (unit event handler sub_42A535).</summary>
    public int Salary { get; set; }
    /// <summary>Money a collector carries (unit vt+1CCh / vt+1D0h / vt+1D4h).</summary>
    public long CarriedMoney { get; set; }
    public bool IsDead { get; private set; }
    /// <summary>In a police car on the way to the station.</summary>
    public bool IsArrested { get; private set; }
    /// <summary>The order the unit is carrying out (extortion…); replaced by the next order.</summary>
    public Node CurrentOrder { get; private set; }

    /// <summary>Raised once when the unit dies or leaves the game (jailed).</summary>
    public event Action<Unit> Died;

    public Vector2I Cell => Map.LocalToMap(Position);
    public bool IsMoving => _astarNavPath.Count > 0 || _targetPositionNext != NoTarget;

    /// <summary>
    /// vt+1ACh (sub_42E385): vision radius now — unit table +2Ch while walking (activity +0BCh 1–4), +2Ah otherwise
    /// (the Sniper sees and so shoots 1 tile farther standing: 8 / 9). TODO: activities 12+ shrink it to
    /// +2Ah × (27 − activity) / 15 (flt_4849E0 / flt_4849E4), at least 1.
    /// </summary>
    public int CurrentVision
    {
        get
        {
            var stats = _unitInstance?.Stats;
            if (stats == null)
                return _unitInstance?.VisionRadius ?? 8;
            return IsMoving ? stats.Vision : stats.VisionMax;
        }
    }

    private bool[] _sightGrid;
    private Vector2I _sightCell = new(int.MinValue, int.MinValue);
    private int _sightRadius = -1;

    /// <summary>
    /// The sight grid (+0E4h), rebuilt like the unit tick does (0x42AB79) only when the cell or the radius changed.
    /// </summary>
    public bool[] SightGrid(out Vector2I center, out int radius)
    {
        center = Map.LocalToMap(Position);
        radius = CurrentVision;
        if (_sightGrid == null || center != _sightCell || radius != _sightRadius)
        {
            _sightGrid = Vision.Build(center, radius);
            _sightCell = center;
            _sightRadius = radius;
        }
        return _sightGrid;
    }

    /// <summary>Is <paramref name="other"/>'s cell visible in this unit's sight grid (sub_42B750)?</summary>
    public bool Sees(Node2D other)
    {
        var grid = SightGrid(out var center, out var r);
        return Vision.Visible(grid, center, r, Map.LocalToMap(other.Position));
    }

    public void SetType(UnitCommon.UnitType type)
    {
        UnitType = type;
        if (_unitInstance != null)
        {
            RemoveChild(_unitInstance);
            _unitInstance.QueueFree();
        }
        _unitInstance = new UnitCommon(type) { OwnerType = _unitOwner };
        AddChild(_unitInstance);
        Salary = _unitInstance.Stats?.Cost ?? 0;
    }

    public void SetOwner(UnitCommon.UnitOwner owner)
    {
        _unitOwner = owner;
        if (_unitInstance != null)
            _unitInstance.OwnerType = owner;
    }

    /// <summary>The unit joins <paramref name="team"/> (unit vt+74h with a new owner, e.g. a bought snitch).</summary>
    public void ChangeOwner(Team team)
    {
        CancelOrder();
        StopMoving();
        if (CanAttack != null)
            CanAttack.Target = null;
        Selected = false;
        TeamColor = team.TeamColor;
        var relation = Teams.GetCurrentPlayerTeam()?.GetTargetRelation(team.TeamColor);
        SetOwner(relation switch
        {
            Teams.TeamRelation.Self => UnitCommon.UnitOwner.Player,
            Teams.TeamRelation.Friend => UnitCommon.UnitOwner.Teammate,
            _ => UnitCommon.UnitOwner.Enemy,
        });
        IsDeserter = false;
        CanAttack?.Reinit();
    }

    #region Orders

    /// <summary>Start a new order (a node that drives this unit); the previous one is cancelled.</summary>
    public void SetOrder(Node order)
    {
        CancelOrder();
        CurrentOrder = order;
        if (order != null)
            AddChild(order);
    }

    public void CancelOrder()
    {
        if (CurrentOrder != null && IsInstanceValid(CurrentOrder))
            CurrentOrder.QueueFree();
        CurrentOrder = null;
    }

    /// <summary>Called by an order node when it is done.</summary>
    public void OrderFinished(Node order)
    {
        if (CurrentOrder == order)
            CurrentOrder = null;
        if (IsInstanceValid(order))
            order.QueueFree();
    }

    #endregion

    #region Movement targets

    private static readonly Vector2I NoTarget = new(int.MaxValue, int.MaxValue);
    private Vector2I _targetPositionNext = NoTarget;
    private Vector2I _targetPosition;

    /// <summary>Current destination cell.</summary>
    public Vector2I Target => _targetPosition;

    /// <summary>Player order: walk to a point given in the parent's (main scene's) coordinates.</summary>
    public void MoveTo(Vector2 localPosition)
    {
        CancelOrder();
        TargetStructure = null;
        MoveMode = null;
        WalkToCell(Map.LocalToMap(localPosition));
    }

    /// <summary>Walk to a cell (or next to it, if it is a building). Used by orders; doesn't touch the current order.</summary>
    public void WalkToCell(Vector2I cell) => _targetPositionNext = cell;

    /// <summary>Walk next to a map object.</summary>
    public void WalkTo(Node2D target) => WalkToCell(Map.LocalToMap(target.Position));

    public void StopMoving()
    {
        _targetPositionNext = NoTarget;
        _astarNavPath.Clear();
    }

    private Node2D _targetStructure;
    /// <summary>
    /// Player order on a map object: a company is extorted (<see cref="ExtortOrder"/>), a hostile unit is attacked.
    /// </summary>
    public Node2D TargetStructure
    {
        get => _targetStructure;
        set
        {
            _targetStructure = value;
            MoveMode = null;
            if (CanAttack != null)
                CanAttack.Target = null;
            if (value == null)
                return;

            if (value is Company company)
            {
                if (ExtortOrder.CanExtort(this, company))
                    SetOrder(new ExtortOrder(this, company));
                else
                {
                    CancelOrder();
                    WalkTo(company);
                }
                return;
            }

            CancelOrder();
            if (CanAttack != null && value is IGameEntity target && target.CanBeAttacked != null)
            {
                var relation = Teams.GetOrAddTeam(TeamColor).GetTargetRelation(target.TeamColor);
                if (relation is Teams.TeamRelation.Enemy or Teams.TeamRelation.Neutral)
                    CanAttack.Target = target.CanBeAttacked;
            }
            WalkTo(value);
        }
    }

    private List<Vector2I> _astarNavPath = new();

    private void CalcAstar(Vector2I target)
    {
        if (!_astarNav.IsInBoundsv(target))
            return;
        var current = Map.LocalToMap(Position);
        var isSolidTarget = _astarNav.IsPointSolid(target);
        if (isSolidTarget)
            _astarNav.SetPointSolid(target, false);

        var path = new List<Vector2I>(_astarNav.GetIdPath(current, target, true));
        if (path.Count > 0)
            path.RemoveAt(0);
        if (isSolidTarget)
        {
            _astarNav.SetPointSolid(target, true);
            // Stop next to a building instead of walking into it.
            if (path.Count > 0)
                path.RemoveAt(path.Count - 1);
        }
        _astarNavPath = path;
    }

    #endregion

    public override void _Ready()
    {
        AddToGroup(GroupName);
        _selection = FindChild("Selection") as Selection;

        Map = (GetParent() as Main)?.Map ?? MapTileMath.Tilemap;
        _astarNav = new AStarGrid2D
        {
            CellShape = AStarGrid2D.CellShapeEnum.IsometricRight,
            CellSize = MapTileMath.StandartTile,
            DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never,
            Region = Map.GetUsedRect(),
        };
        _astarNav.Update();
        foreach (var coords in Map.GetMeta(TileMapGenerator.TileNonWalkable).AsGodotArray<Vector2I>())
            _astarNav.SetPointSolid(coords);

        _as2d = FindChild("Animation") as AnimatedSprite2D;
        _shape = FindChild("Shape") as CollisionShape2D;

        // Scene sub-resources are shared between instances; give each unit its own copy.
        _as2d.SpriteFrames = (SpriteFrames)_as2d.SpriteFrames.Duplicate();
        _shape.Shape = (Shape2D)_shape.Shape.Duplicate();

        _unitInstance.FillSpriteFrames(_as2d.SpriteFrames);
        var idle = OriginalUnitSprites.IdleName;
        if (_shape.Shape is CapsuleShape2D capsule && _as2d.SpriteFrames.HasAnimation(idle)
            && _as2d.SpriteFrames.GetFrameCount(idle) > 0)
        {
            var imageIdle = _as2d.SpriteFrames.GetFrameTexture(idle, 0);
            capsule.Height = imageIdle.GetHeight();
            capsule.Radius = imageIdle.GetWidth() / 2;
        }
        _animStopped = false;
        _animMove = true;
        AnimationStop();

        if (CanBeAttacked != null)
            CanBeAttacked.HPisZERO += OnHPisZero;

        StartPayroll();
    }

    #region Animation

    private bool _animStopped;
    private bool _animMove;
    private bool _animAttack;
    private MapTileMath.Directions _oldDirection = MapTileMath.Directions.SE;

    public void AnimationStop(bool move = true)
    {
        if (_animStopped || IsDead)
            return;
        if ((_animMove && move) || (_animAttack && !move))
        {
            _as2d.Stop();
            _as2d.Frame = 0;
            _as2d.Play(OriginalUnitSprites.IdleName);
            _animStopped = true;
        }
    }

    public void AnimationMoveOrAttack(Vector2 directionAsVector, bool move = true)
    {
        if (IsDead)
            return;
        var direction = MapTileMath.GetDirection(directionAsVector);
        if (!_animStopped && direction == _oldDirection && ((move && _animMove) || (!move && _animAttack)))
            return;
        _oldDirection = direction;
        _animStopped = false;
        _as2d.Stop();
        _animMove = move;
        _animAttack = !move;
        var slType = move ? OriginalUnitSprites.SlType.move : OriginalUnitSprites.SlType.attack;
        _as2d.Play(OriginalUnitSprites.GetAnimLayerName(slType, direction));
    }

    /// <summary>Show one attack swing towards <paramref name="target"/> (used by orders that hit once).</summary>
    public void AnimationAttackOnce(Node2D target)
    {
        AnimationMoveOrAttack(target.Position - Position, false);
        _attackPoseUntil = GameClock.Instance?.Minute + 20 ?? 0;
    }

    private long _attackPoseUntil = -1;

    #endregion

    public override void _Process(double delta)
    {
        if (IsDead || IsArrested)
            return;
        if (_attackPoseUntil >= 0 && (GameClock.Instance?.Minute ?? 0) >= _attackPoseUntil)
        {
            _attackPoseUntil = -1;
            AnimationStop(false);
        }
        ThinkTick();
        if (CanAttack == null)
            return;
        CanAttack.DecreaseAttackTimer(delta);
        if (CanAttack.TargetInRange() && CanAttack.TargetParent != null)
            AnimationMoveOrAttack(CanAttack.TargetParent.Position - Position, false);
        else if (_attackPoseUntil < 0)
            AnimationStop(false);
    }

    private bool _astarPointReached = true;

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead || IsArrested)
            return;
        if (_astarPointReached && _targetPositionNext != NoTarget)
        {
            _targetPosition = _targetPositionNext;
            _targetPositionNext = NoTarget;
            CalcAstar(_targetPosition);
        }
        if (_astarNavPath.Count == 0)
        {
            AnimationStop();
            return;
        }

        _astarPointReached = false;
        var target = Map.MapToLocal(_astarNavPath[0]);
        var next = Position.MoveToward(target, (float)delta * _unitInstance.MoveSpeed);
        AnimationMoveOrAttack(next - Position, true);
        Position = next;

        if (Position == target)
        {
            _astarNavPath.RemoveAt(0);
            _astarPointReached = true;
        }
    }

    #region Salary (sub_42A535: daily salary / 7; a unit that can't be paid resigns)

    private void StartPayroll()
    {
        var clock = GameClock.Instance;
        clock?.ScheduleIn(GameClock.MinutesPerDay, () => PayDay(clock));
    }

    private void PayDay(GameClock clock)
    {
        if (!IsInstanceValid(this) || IsDead)
            return;
        var org = OwnerTeam?.Org;
        if (org != null && Salary > 0 && !IsArrested)
        {
            // sub_42A535: the owner must afford the day's pay within the overdraft (vt+70h), else a gangster quits
            // (a Collector just goes unpaid); Spend itself would always pay while the player has gangsters.
            if (org.CanAfford(Salary / 7))
                org.Spend(Salary / 7);
            else if (UnitType != UnitCommon.UnitType.Collector)
            {
                Resign();
                return;
            }
        }
        clock.ScheduleIn(GameClock.MinutesPerDay, () => PayDay(clock));
    }

    /// <summary>A gangster who quit (owner none): he fights every player's units (unit AI, unowned units).</summary>
    public bool IsDeserter { get; private set; }

    /// <summary>
    /// Unpaid units quit (vt+74h with no owner): they become neutral and hostile to every player's units, the
    /// former owner's included (user, from the original game; unit AI sub_42AA86 treats every owned unit as an enemy
    /// of an unowned one).
    /// </summary>
    public void Resign()
    {
        var wasLocal = OwnerTeam?.IsCurrentPlayerColor == true;
        GD.Print($"Unit {DisplayName} resigns");
        if (wasLocal && ResignSound(UnitType) is { } sound)
            Sound.Play(sound);
        CancelOrder();
        StopMoving();
        if (CanAttack != null)
            CanAttack.Target = null;
        Selected = false;
        TeamColor = Teams.NeutralTeamColor;
        _unitOwner = UnitCommon.UnitOwner.Neutral;
        IsDeserter = true;
        CanAttack?.Reinit();
    }

    public static string ResignSound(UnitCommon.UnitType type) => type switch
    {
        UnitCommon.UnitType.Thug => "ThugResign.wav",
        UnitCommon.UnitType.Pistol => "PistolResign.wav",
        UnitCommon.UnitType.Thompson => "ThompsonResign.wav",
        UnitCommon.UnitType.Terrorist => "BombmanResign.wav",
        UnitCommon.UnitType.Sniper => "SniperResign.wav",
        UnitCommon.UnitType.Shotgun => "ShotgunResign.wav",
        UnitCommon.UnitType.Hitman => "HitmanResign.wav",
        UnitCommon.UnitType.Thief => "ThiefResign.wav",
        _ => null,
    };

    #endregion

    #region Death and arrest

    private void OnHPisZero(Color attackerColor) => Die();

    /// <summary>The unit is killed: it stops, shows its corpse for a while and is removed.</summary>
    public void Die()
    {
        if (IsDead)
            return;
        IsDead = true;
        Selected = false;
        CancelOrder();
        StopMoving();
        if (CanAttack != null)
            CanAttack.Target = null;
        RemoveFromGroup(GroupName);
        _shape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        var killed = OriginalUnitSprites.SpriteLayersDict[OriginalUnitSprites.SpriteLayersEnum.killed];
        if (_as2d != null && _as2d.SpriteFrames.HasAnimation(killed) && _as2d.SpriteFrames.GetFrameCount(killed) > 0)
            _as2d.Play(killed);
        GD.Print($"Unit {DisplayName} ({UnitType}) died");
        Died?.Invoke(this);
        var clock = GameClock.Instance;
        if (clock != null)
            clock.ScheduleIn(CorpseMinutes, () =>
            {
                if (IsInstanceValid(this))
                    QueueFree();
            });
        else
            QueueFree();
    }

    /// <summary>Taken by the police: hidden and inactive until jailed or released.</summary>
    public void Arrest()
    {
        if (IsDead || IsArrested)
            return;
        IsArrested = true;
        Selected = false;
        CancelOrder();
        StopMoving();
        if (CanAttack != null)
            CanAttack.Target = null;
        Visible = false;
        _shape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        GD.Print($"Unit {DisplayName} ({UnitType}) arrested");
    }

    /// <summary>Released by the police at <paramref name="cell"/>.</summary>
    public void Release(Vector2I cell)
    {
        if (!IsArrested)
            return;
        IsArrested = false;
        Visible = true;
        Position = Map.MapToLocal(cell);
        _shape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
    }

    /// <summary>Jailed for good (unit vt+1A0h): the unit leaves the game.</summary>
    public void Jail() => RemoveFromGame("jailed");

    /// <summary>The owner was eliminated: the unit leaves the game (default).</summary>
    public void Vanish() => RemoveFromGame("left with its eliminated owner");

    /// <summary>A unit of an eliminated owner kept as a neutral that fights everyone, other rogues included.</summary>
    public bool IsRogue { get; private set; }

    /// <summary>The owner was eliminated and <see cref="Organisation.RogueUnits"/> is on.</summary>
    public void GoRogue()
    {
        GD.Print($"Unit {DisplayName} ({UnitType}) goes rogue");
        CancelOrder();
        StopMoving();
        if (CanAttack != null)
            CanAttack.Target = null;
        Selected = false;
        TeamColor = Teams.NeutralTeamColor;
        _unitOwner = UnitCommon.UnitOwner.Neutral;
        IsRogue = true;
        CanAttack?.Reinit();
    }

    /// <summary>A collector with nothing left to collect leaves the map (vt+1E8h).</summary>
    public void Leave() => RemoveFromGame("left the map");

    private void RemoveFromGame(string reason)
    {
        if (IsDead)
            return;
        IsDead = true;
        Selected = false;
        RemoveFromGroup(GroupName);
        GD.Print($"Unit {DisplayName} ({UnitType}) {reason}");
        Died?.Invoke(this);
        QueueFree();
    }

    #endregion
}
