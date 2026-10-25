using Godot;

namespace Capone;

/// <summary>Money-carrying component (collector, thief, Cadillac, companies).</summary>
public partial class CanHoldMoney : Node, IJsonConfigurable
{
    public const string JsonKey = "can_hold_money";

    [Export] public int MaxMoney { get; set; } = 1000;

    public int HoldedMoney { get; set; }

    public void ApplyJsonValue(string key, Variant value)
    {
        switch (key)
        {
            case "maxMoney": MaxMoney = JsonUtil.ToInt(value); break;
            default: GD.PushWarning($"CanHoldMoney: unknown json key '{key}'"); break;
        }
    }

    public void GiveMoneyToThis(int value) => HoldedMoney = Mathf.Min(HoldedMoney + value, MaxMoney);

    public void GiveMoneyFromThis(int value) => HoldedMoney = Mathf.Max(0, HoldedMoney - value);

    /// <summary>Adds up to the free capacity; returns what was actually added.</summary>
    public int AddMoney(int amount)
    {
        var actual = Mathf.Min(amount, MaxMoney - HoldedMoney);
        if (actual > 0)
            HoldedMoney += actual;
        return actual;
    }

    /// <summary>Removes up to what is held; returns what was actually removed.</summary>
    public int RemoveMoney(int amount)
    {
        var actual = Mathf.Min(amount, HoldedMoney);
        HoldedMoney -= actual;
        return actual;
    }

    public int GetMoney() => HoldedMoney;

    public int GetMaxMoney() => MaxMoney;

    public float GetPercent() => MaxMoney <= 0 ? 0f : HoldedMoney * 1.0f / MaxMoney;

    public bool HasMoney() => HoldedMoney > 0;
}
