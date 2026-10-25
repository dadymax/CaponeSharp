using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>A unit on the map: movement over an A* grid, animation, and its UnitCommon data/components.</summary>
public partial class Unit : CharacterBody2D, ISelectable, IGameEntity
{
    public const string GroupName = "units";

    private Selection _selection;
    public bool Selectable => true;
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
    public TileMapLayer Map { get; private set; }

    private AStarGrid2D _astarNav;
    private CollisionShape2D _shape;
    private AnimatedSprite2D _as2d;

    private UnitCommon _unitInstance;
    private UnitCommon.UnitOwner _unitOwner;
    private CollectorAI _collectorAI;

    public UnitCommon UnitData => _unitInstance;
    public UnitCommon.UnitType UnitType { get; private set; }
    public UnitCommon.UnitOwner UnitOwner => _unitOwner;

    public CanAttack CanAttack => _unitInstance?.CanAttack;
    public CanBeAttacked CanBeAttacked => _unitInstance?.CanBeAttacked;
    public CanHoldMoney CanHoldMoney => _unitInstance?.CanHoldMoney;

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

        if (type is UnitCommon.UnitType.Collector or UnitCommon.UnitType.CollectorWithMoney && _collectorAI == null)
        {
            _collectorAI = new CollectorAI();
            AddChild(_collectorAI);
        }
    }

    public void SetOwner(UnitCommon.UnitOwner owner)
    {
        _unitOwner = owner;
        if (_unitInstance != null)
            _unitInstance.OwnerType = owner;
    }

    #region Movement targets

    private static readonly Vector2I NoTarget = new(int.MaxValue, int.MaxValue);
    private Vector2I _targetPositionNext = NoTarget;
    private Vector2I _targetPosition;

    /// <summary>Current destination cell.</summary>
    public Vector2I Target => _targetPosition;

    /// <summary>Walk to a point given in the parent's (main scene's) coordinates.</summary>
    public void MoveTo(Vector2 localPosition)
    {
        _targetPositionNext = Map.LocalToMap(localPosition);
        TargetStructure = null;
    }

    private Node2D _targetStructure;
    /// <summary>Walk to a company/unit and attack it if hostile.</summary>
    public Node2D TargetStructure
    {
        get => _targetStructure;
        set
        {
            _targetStructure = value;
            if (value == null)
            {
                if (CanAttack != null)
                    CanAttack.Target = null;
                return;
            }

            if (CanAttack != null && value is IGameEntity target)
            {
                var relation = Teams.GetOrAddTeam(TeamColor).GetTargetRelation(target.TeamColor);
                if (relation is Teams.TeamRelation.Enemy or Teams.TeamRelation.Neutral)
                {
                    if (target.CanBeAttacked != null)
                        CanAttack.Target = target.CanBeAttacked;
                }
                else if (relation == Teams.TeamRelation.Self)
                {
                    // TODO: can embark into limousine. Maybe needs to move out of CanAttack != null for thief.
                }
            }
            _targetPositionNext = Map.LocalToMap(value.Position);
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
        if (path.Count > 0)
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
    }

    #region Animation

    private bool _animStopped;
    private bool _animMove;
    private bool _animAttack;
    private MapTileMath.Directions _oldDirection = MapTileMath.Directions.SE;

    public void AnimationStop(bool move = true)
    {
        if (_animStopped)
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

    #endregion

    public override void _Process(double delta)
    {
        if (CanAttack == null)
            return;
        CanAttack.DecreaseAttackTimer(delta);
        if (CanAttack.TargetInRange() && CanAttack.TargetParent != null)
            AnimationMoveOrAttack(CanAttack.TargetParent.Position - Position, false);
        else
            AnimationStop(false);
    }

    private bool _astarPointReached = true;

    public override void _PhysicsProcess(double delta)
    {
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

    private void OnHPisZero(Color attackerColor)
    {
        // TODO: unit death animation
        // TODO: remove from the scene (after some tick?)
        if (_collectorAI != null)
        {
            _collectorAI.QueueFree();
            _collectorAI = null;
        }
    }

    /// <summary>Switch the collector between autopilot and manual control.</summary>
    public void ToggleCollectorMode() => _collectorAI?.ToggleControlMode();

    /// <summary>Send the collector to the HQ now.</summary>
    public void ForceCollectorReturn() => _collectorAI?.ForceReturnToHQ();
}
