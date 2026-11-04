using Godot;

namespace Capone;

/// <summary>
/// The Thief's job (vtable off_4855A8, constructor sub_42FC60, arrival step vt+88h sub_42F6E0). A Thief sent onto any
/// company or HQ (Unit::GiveTargetedOrder sub_42C1F6, case 0 with unit type 0Bh — he never extorts) walks next to it,
/// then:
/// <list type="bullet">
/// <item>Own HQ, or own cash-out business (catalogue +28h = 3): delivers the money he carries to his owner's account
/// (HQ vt+68h, <see cref="Organisation.Deposit"/>); the local owner reads "%s brought $%ld." (29,22).</item>
/// <item>Another player's company: takes its uncollected money (vt+184h).</item>
/// <item>Another player's HQ: takes all of that player's cash, if positive (paid with vt+6Ch).</item>
/// <item>Unowned companies give nothing.</item>
/// </list>
/// After a theft the thief's local owner reads "%s lost $%s." / "$%s stolen from %s." (29,20, victim's name), the
/// local victim "%s stole $%s." (29,21, thief owner's name), with ThiefSteal.wav. The job then ends; the player walks
/// the thief home to deliver. A killed thief's money goes to his killer (<see cref="Unit.LoseCarriedMoney"/>).
/// </summary>
public partial class StealOrder : Node
{
    private readonly Unit _thief;
    private readonly Company _target;
    private bool _walking;
    private bool _finished;

    public StealOrder() { }

    public StealOrder(Unit thief, Company target)
    {
        _thief = thief;
        _target = target;
        Name = "StealOrder";
    }

    public Company Target => _target;

    public static bool CanSteal(Unit unit) => unit?.UnitType == UnitCommon.UnitType.Thief && unit.OwnerTeam != null;

    public override void _Ready()
    {
        if (SaveGame.Restoring)
            return;
        _thief.WalkTo(_target);
        _walking = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_walking || _finished || _thief.IsMoving)
            return;
        _walking = false;
        Arrive();
        Finish();
    }

    /// <summary>sub_42F6E0.</summary>
    private void Arrive()
    {
        if (!IsInstanceValid(_thief) || _thief.IsDead || _thief.IsArrested || !IsInstanceValid(_target)
            || MapTileMath.GetDistanceExact(_thief, _target) > CollectorJob.ReachDistance)
            return;
        var owner = _thief.OwnerTeam;
        var victim = _target.OwnerTeam;
        if (owner == null || victim == null)
            return;
        if (victim == owner)
        {
            if (_target.IsHq || _target.Entry?.Value28 == 3)
                Deliver(owner);
            return;
        }
        long amount = 0;
        if (_target.IsHq)
        {
            var cash = victim.Org?.Cash ?? 0;
            if (cash > 0 && victim.Org.Spend(cash))
                amount = cash;
        }
        else if (_target.IsEconomic)
            amount = _target.TakeUncollected();
        if (amount == 0)
            return;
        _thief.CarriedMoney += amount;
        GD.Print($"Thief of {owner.PlayerName} stole {amount} from {victim.PlayerName} ({_target.KindName})");
        if (owner.IsCurrentPlayerColor)
        {
            Sound.Play("ThiefSteal.wav");
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(29, 20), victim.PlayerName, PanelInfo.Money(amount)));
        }
        else if (victim.IsCurrentPlayerColor)
        {
            Sound.Play("ThiefSteal.wav");
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(29, 21), owner.PlayerName, PanelInfo.Money(amount)));
        }
    }

    private void Deliver(Team owner)
    {
        var amount = _thief.CarriedMoney;
        if (amount <= 0 || owner.Org == null)
            return;
        _thief.CarriedMoney = 0;
        owner.Org.Deposit(amount);
        GD.Print($"Thief of {owner.PlayerName} brought {amount}: {owner.Org}");
        if (owner.IsCurrentPlayerColor)
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(29, 22), _thief.DisplayName, amount));
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        if (IsInstanceValid(_thief))
            _thief.OrderFinished(this);
    }
}
