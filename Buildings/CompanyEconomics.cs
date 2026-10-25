using Godot;

namespace Capone;

/// <summary>Company cash: income accrues into CurrentValue until a collector takes it.</summary>
public class CompanyEconomics
{
    public int CurrentValue { get; set; }

    private int _income;
    /// <summary>Income per period (_incomePeriod ticks of _incomeRate seconds).</summary>
    public int Income
    {
        get => _income;
        set
        {
            _income = value;
            _incomeValuePerRate = value / _incomePeriod;
        }
    }

    private int _incomeRate = 1;
    private int _incomePeriod = 100;
    private int _incomeValuePerRate;
    private double _currentPeriod;

    public void ApplyJsonValue(string key, Variant value)
    {
        switch (key)
        {
            case "Income": Income = JsonUtil.ToInt(value); break;
            case "CurrentValue": CurrentValue = JsonUtil.ToInt(value); break;
            // "Subtypes" is read by CompanyCommon.
        }
    }

    public void Process(double delta)
    {
        _currentPeriod = Mathf.Max(0, _currentPeriod - delta);
        if (_currentPeriod == 0)
        {
            _currentPeriod = _incomeRate;
            CurrentValue += _incomeValuePerRate;
        }
    }

    public void GetMoneyToCourier(CanHoldMoney moneyHolder)
    {
        moneyHolder.GiveMoneyToThis(CurrentValue);
        CurrentValue = 0;
    }
}
