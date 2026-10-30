using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The move job (sub_4284B0, step sub_4279E7): one tile per step toward the destination, deciding at every cell with
/// <see cref="MoveMap.NextDirection"/>; a step takes the unit's table time (+18h, game minutes per tile: 60, the
/// Cadillac 30). Stuck: unless this cell was visited twice lately, a detour waypoint is searched
/// (<see cref="MoveMap.FindDetour"/>) and walked to first; stuck again on the way → the move ends. The last six
/// waypoints / stuck cells are remembered (+3Ch, sub_427940 / sub_4279B0). Arrived next to a building or an occupied
/// destination → the move ends.
/// </summary>
public partial class Unit
{
    private const int VisitedSlots = 6;

    private Node2D _moveTargetNode;
    private Vector2I _moveTarget;
    private bool _moving;
    private Vector2I _lineStart;
    private Vector2I? _detour;
    private Vector2I? _stepCell;
    private readonly Vector2I[] _visited = new Vector2I[VisitedSlots];
    private int _visitedCount;

    /// <summary>The cell the unit stands on, or steps into (for other units' occupancy).</summary>
    public Vector2I MoveCell => _stepCell ?? Cell;

    /// <summary>Puts the unit on a point at once (jail release, a car, tests): no move and no half-done step.</summary>
    public void PlaceAt(Vector2 position)
    {
        StopMoving();
        _stepCell = null;
        Position = position;
    }

    private void StartMove()
    {
        _moving = true;
        _lineStart = Cell;
        _detour = null;
        _visitedCount = 0;
    }

    private void Remember(Vector2I cell)
    {
        for (var i = VisitedSlots - 1; i > 0; i--)
            _visited[i] = _visited[i - 1];
        _visited[0] = cell;
        _visitedCount = Mathf.Min(_visitedCount + 1, VisitedSlots);
    }

    private int TimesVisited(Vector2I cell)
    {
        var n = 0;
        for (var i = 0; i < _visitedCount; i++)
            if (_visited[i] == cell)
                n++;
        return n;
    }

    /// <summary>sub_4279E7: the direction of the next step, or Arrived / Stuck.</summary>
    private int NextMoveDirection()
    {
        var here = Cell;
        var destination = Target;
        var goal = _detour ?? destination;
        var direction = MoveMap.NextDirection(here, goal, _lineStart, destination, RoadOnly, this);
        if (direction == MoveMap.Arrived)
        {
            if (_detour == null)
                return MoveMap.Arrived;
            _detour = null;
            direction = MoveMap.NextDirection(here, destination, _lineStart, destination, RoadOnly, this);
        }
        if (direction != MoveMap.Stuck)
            return direction;
        if (_detour != null || TimesVisited(here) > 1)
            return MoveMap.Stuck;
        var waypoint = MoveMap.FindDetour(here, destination, RoadOnly, c => TimesVisited(c) > 0, this);
        if (waypoint is not { } w)
            return MoveMap.Stuck;
        _detour = w;
        Remember(w);
        Remember(here);
        return MoveMap.NextDirection(here, w, _lineStart, destination, RoadOnly, this);
    }

    // ---- occupancy of cells (tile vt+134h occupant count, vt+130h occupant), shared by all units ------------------

    private static ulong _occupancyFrame = ulong.MaxValue;
    private static readonly Dictionary<Vector2I, List<Unit>> Occupants = new();

    /// <summary>Wires <see cref="MoveMap.Occupancy"/> to the units on the map.</summary>
    public static void InitOccupancy() => MoveMap.Occupancy = CellOccupancy;

    private static (int Count, bool Moving, bool Fighting) CellOccupancy(Vector2I cell, object self)
    {
        if (!UnitsOn(cell, out var here))
            return (0, false, false);
        var others = here.Where(u => u != self).ToList();
        return others.Count == 0 ? (0, false, false) : (others.Count, others[0].IsMoving, others.Any(u => u.IsFighting));
    }

    /// <summary>The units on a map cell (tile vt+88h list), from a cache refreshed once per physics frame.</summary>
    private static bool UnitsOn(Vector2I cell, out List<Unit> units)
    {
        var frame = Engine.GetPhysicsFrames();
        if (frame != _occupancyFrame && Engine.GetMainLoop() is SceneTree tree)
        {
            _occupancyFrame = frame;
            Occupants.Clear();
            foreach (var unit in tree.GetNodesInGroup(GroupName).OfType<Unit>())
            {
                if (!IsInstanceValid(unit) || unit.IsDead || unit.IsArrested || unit.InsideBuilding || unit.Map == null)
                    continue;
                var at = unit.MoveCell;
                if (!Occupants.TryGetValue(at, out var list))
                    Occupants[at] = list = new List<Unit>(1);
                list.Add(unit);
            }
        }
        return Occupants.TryGetValue(cell, out units);
    }

    // ---- spreading out (unit vt+15Ch sub_42B500, vt+160h sub_42B490) ---------------------------------------------

    /// <summary>Unit +0A8h = 0: no job (order or move) runs for the unit.</summary>
    public bool HasNoJob => CurrentOrder == null && !IsMoving;

    /// <summary>sub_42B490: the unit may stay on <paramref name="cell"/> unless another living unit there has no job.</summary>
    public bool MayStayOn(Vector2I cell) =>
        !UnitsOn(cell, out var units) || !units.Any(u => u != this && !u.IsDead && u.HasNoJob);

    /// <summary>
    /// sub_42B500, from the idle branch of the hourly think: a unit without a job, not in a car, sharing its cell with
    /// another idle living unit steps to a neighbour (4 directions, random start, tables dword_484604..10): first a road
    /// or pavement cell (tile vt+158h) it may stay on, else any road or pavement cell. So units sent to one cell end
    /// up one per cell around it.
    /// </summary>
    public void SpreadOut()
    {
        if (IsDead || IsArrested || InsideBuilding || Vehicle != null || !HasNoJob || Map == null)
            return;
        var here = Cell;
        if (MayStayOn(here))
            return;
        var start = MoveMap.Rng.Next(4);
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < 4; i++)
            {
                var next = MoveMap.Step(here, (start + i) % 4);
                if (!MoveMap.IsRoadOrPavement(next) || !MoveMap.Walkable(next, RoadOnly))
                    continue;
                if (pass == 0 && !MayStayOn(next))
                    continue;
                MoveTo(Map.MapToLocal(next));
                // The step puts the unit on the next tile at once (sub_439690), so a unit thinking after it this
                // frame already sees it there.
                if (Occupants.TryGetValue(here, out var list))
                    list.Remove(this);
                if (!Occupants.TryGetValue(next, out var there))
                    Occupants[next] = there = new List<Unit>(1);
                there.Add(this);
                return;
            }
    }
}
