using Godot;

namespace Capone;

/// <summary>
/// Collector autopilot: walks to the player's companies, empties their cash and carries it to the HQ.
/// Child of the Unit node.
/// </summary>
public partial class CollectorAI : Node
{
    [Signal] public delegate void MoneyDeliveredEventHandler(int amount);

    public enum ControlMode { Auto, Manual }
    public enum State { Idle, GoingToCompany, Collecting, GoingToHQ }

    public ControlMode Mode { get; set; } = ControlMode.Auto;
    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>Go home once the bag is this full (0..1).</summary>
    public float ReturnThreshold { get; set; } = 0.8f;
    /// <summary>Seconds between collection ticks.</summary>
    public float CollectRate { get; set; } = 0.5f;

    private Unit _unitParent;
    private CanHoldMoney _canHoldMoney;
    private Company _targetCompany;
    private Company _hqCompany;
    private float _collectTimer;

    public override void _Ready()
    {
        _unitParent = GetParent() as Unit;
        _canHoldMoney = _unitParent?.CanHoldMoney;
    }

    public override void _Process(double delta)
    {
        if (Mode != ControlMode.Auto || _unitParent == null)
            return;

        switch (CurrentState)
        {
            case State.Idle: HandleIdle(); break;
            case State.GoingToCompany: HandleMovingToCompany(); break;
            case State.Collecting: HandleCollecting(delta); break;
            case State.GoingToHQ: HandleGoingToHQ(); break;
        }
    }

    /// <summary>Player company with the best income/distance ratio.</summary>
    public Company FindBestCompany()
    {
        Company best = null;
        var bestScore = -1.0f;
        foreach (var node in GetTree().GetNodesInGroup(Company.GroupName))
        {
            if (node is not Company company || company.CompanyOwner != CompanyCommon.CompanyOwner.Player)
                continue;
            if (company.CompanyType == CompanyCommon.CompanyType.HQ || company.CompanyEconomics == null)
                continue;
            var distance = MapTileMath.GetDistance(_unitParent, company);
            var score = company.CompanyEconomics.Income / Mathf.Max(distance, 1.0f);
            if (score > bestScore)
            {
                bestScore = score;
                best = company;
            }
        }
        return best;
    }

    public Company FindHQ()
    {
        foreach (var node in GetTree().GetNodesInGroup(Company.GroupName))
            if (node is Company company
                && company.CompanyOwner == CompanyCommon.CompanyOwner.Player
                && company.CompanyType == CompanyCommon.CompanyType.HQ)
                return company;
        return null;
    }

    private void HandleIdle()
    {
        if (_canHoldMoney != null && _canHoldMoney.HasMoney()
            && _canHoldMoney.GetMoney() >= _canHoldMoney.GetMaxMoney() * ReturnThreshold)
        {
            _hqCompany = FindHQ();
            if (_hqCompany != null)
            {
                GoTo(_hqCompany, State.GoingToHQ);
                return;
            }
        }

        _targetCompany = FindBestCompany();
        if (_targetCompany != null)
            GoTo(_targetCompany, State.GoingToCompany);
    }

    private void HandleMovingToCompany()
    {
        if (!IsInstanceValid(_targetCompany))
        {
            CurrentState = State.Idle;
            return;
        }
        if (MapTileMath.GetDistance(_unitParent, _targetCompany) <= 1)
        {
            CurrentState = State.Collecting;
            _collectTimer = 0f;
        }
    }

    private void HandleCollecting(double delta)
    {
        if (!IsInstanceValid(_targetCompany) || !_targetCompany.IsInsideTree()
            || _targetCompany.CompanyOwner != CompanyCommon.CompanyOwner.Player)
        {
            CurrentState = State.Idle;
            return;
        }

        _collectTimer -= (float)delta;
        if (_collectTimer > 0)
            return;

        var economics = _targetCompany.CompanyEconomics;
        if (economics != null && economics.CurrentValue > 0 && _canHoldMoney != null)
            economics.GetMoneyToCourier(_canHoldMoney);
        _collectTimer = CollectRate;

        if (_canHoldMoney != null && _canHoldMoney.GetPercent() >= ReturnThreshold)
        {
            _hqCompany = FindHQ();
            if (_hqCompany != null)
            {
                _targetCompany = null;
                GoTo(_hqCompany, State.GoingToHQ);
                return;
            }
        }
        // Nothing left here: look for the next company.
        if (economics == null || economics.CurrentValue == 0)
            CurrentState = State.Idle;
    }

    private void HandleGoingToHQ()
    {
        if (!IsInstanceValid(_hqCompany))
        {
            CurrentState = State.Idle;
            return;
        }
        if (MapTileMath.GetDistance(_unitParent, _hqCompany) > 1)
            return;

        if (_canHoldMoney != null)
        {
            var money = _canHoldMoney.GetMoney();
            if (money > 0)
            {
                _canHoldMoney.RemoveMoney(money);
                var team = Teams.GetOrAddTeam(_unitParent.TeamColor);
                Teams.AddPlayerMoney(team.PlayerId, money);
                EmitSignal(SignalName.MoneyDelivered, money);
            }
        }
        CurrentState = State.Idle;
        _hqCompany = null;
    }

    private void GoTo(Company company, State state)
    {
        CurrentState = state;
        _unitParent.MoveTo(company.Position);
    }

    public void ForceReturnToHQ()
    {
        if (Mode != ControlMode.Auto || _canHoldMoney == null || _canHoldMoney.GetMoney() <= 0)
            return;
        _hqCompany = FindHQ();
        if (_hqCompany != null)
        {
            _targetCompany = null;
            GoTo(_hqCompany, State.GoingToHQ);
        }
    }

    public void ToggleControlMode() =>
        Mode = Mode == ControlMode.Auto ? ControlMode.Manual : ControlMode.Auto;

    public string GetStateString() => CurrentState switch
    {
        State.Idle => "Idle",
        State.GoingToCompany => "GoingToCompany",
        State.Collecting => "Collecting",
        State.GoingToHQ => "ReturningToHQ",
        _ => "Unknown",
    };
}
