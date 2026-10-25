using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>A sequence of scenarios and the index of the current one.</summary>
public class Campaign
{
    public string CampaignName { get; set; } = "";
    public List<Scenario> Scenarios { get; } = new();
    public int CurrentScenarioIndex { get; private set; }

    public void AddScenario(Scenario scenario) => Scenarios.Add(scenario);

    public Scenario LoadScenario(int index)
    {
        if (index < 0 || index >= Scenarios.Count)
        {
            GD.PrintErr("Campaign: invalid scenario index: ", index);
            return null;
        }
        CurrentScenarioIndex = index;
        return Scenarios[index];
    }

    public bool LoadNextScenario()
    {
        if (CurrentScenarioIndex + 1 >= Scenarios.Count)
            return false;
        CurrentScenarioIndex++;
        return true;
    }

    public bool LoadPreviousScenario()
    {
        if (CurrentScenarioIndex - 1 < 0)
            return false;
        CurrentScenarioIndex--;
        return true;
    }

    public bool IsCampaignComplete() => CurrentScenarioIndex >= Scenarios.Count - 1;

    public Scenario GetCurrentScenario() => Scenarios.Count == 0 ? null : Scenarios[CurrentScenarioIndex];

    public int GetScenarioCount() => Scenarios.Count;
}
