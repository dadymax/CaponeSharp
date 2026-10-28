using System;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// A company on the map: the original company class (vtable off_4803CC, constructor fnSetBuildingType 0x420C90).
/// Its kind comes from the business catalogue (<see cref="Entry"/>); signs/sprites and the health component live in a
/// <see cref="CompanyCommon"/> child.
/// <para>Economy (event handler vt+2Ch, sub_420ED5):</para>
/// <list type="bullet">
/// <item>Every hour: cache whether the block lets the owner earn here; unowned or legal companies heal 5 HP.</item>
/// <item>441 times a week (every ~22 min, random offset): <see cref="Uncollected"/> += <see cref="Income"/> / 441;
/// support businesses (negative income) charge their owner instead.</item>
/// <item>Weekly: unowned companies lose their uncollected cash.</item>
/// </list>
/// Income (vt+188h, sub_422AF4) = catalogue income per week × damage factor × district factor (legal only) × owner's
/// type multiplier.
/// </summary>
public partial class Company : StaticBody2D, ISelectable, IGameEntity
{
    public const string GroupName = "companies";
    /// <summary>Income is credited this many times per week (0x1B9).</summary>
    public const int IncomeTicksPerWeek = 441;
    public const int HealPerHour = 5;
    /// <summary>A company gives in to extortion below this share of its HP (dbl_4803A0).</summary>
    public const float SurrenderHpRatio = 0.15f;

    public bool Selectable { get; set; } = true;
    private Selection _selection;
    public bool Selected
    {
        get => _selection?.Enabled ?? false;
        set
        {
            if (_selection != null)
                _selection.Enabled = value;
        }
    }

    public CanAttack CanAttack => null;
    public CanBeAttacked CanBeAttacked => _company?.CanBeAttacked;
    public CanHoldMoney CanHoldMoney => null;
    public CompanyCommon CompanyData => _company;

    // ---- identity ----------------------------------------------------------------------------------------------------

    /// <summary>Catalogue row (null for HQ, police station and other non-economic buildings).</summary>
    public OriginalBusinessTable.Entry Entry { get; private set; }
    public CityBlock Block { get; set; }
    /// <summary>Random company name (fnGetRandomParameters list 2).</summary>
    public string CompanyName { get; set; } = "";
    /// <summary>Map cell the company stands on.</summary>
    public Vector2I Cell { get; set; }
    public bool IsHq { get; private set; }
    public bool IsEconomic => Entry != null && !IsHq;
    public OriginalBusinessTable.BusinessType BusinessType => Entry?.Type ?? OriginalBusinessTable.BusinessType.Legal;
    /// <summary>Display name: the business kind from the catalogue ("Пиццерия", "Пивной бар"…).</summary>
    public string KindName => IsHq ? OriginalTexts.Instance.Get(5, 25) : Entry?.Name ?? "";

    // ---- ownership ---------------------------------------------------------------------------------------------------

    public Team OwnerTeam { get; private set; }
    public Color TeamColor => OwnerTeam?.TeamColor ?? Teams.NeutralTeamColor;
    public CompanyCommon.CompanyOwner CompanyOwner { get; private set; } = CompanyCommon.CompanyOwner.Neutral;

    // ---- economy -----------------------------------------------------------------------------------------------------

    /// <summary>Money waiting for the collector (+0A8h).</summary>
    public long Uncollected { get; set; }
    /// <summary>+0CCh: hourly cache of "the block lets the owner earn here".</summary>
    public bool CachedIncomeEnabled { get; private set; } = true;
    /// <summary>+0BCh: income percentage, reset to 100 every week.</summary>
    public int Percent { get; set; } = 100;
    /// <summary>+0C4h: frozen businesses earn nothing.</summary>
    public bool Frozen { get; set; }

    private CompanyCommon _company;
    private AnimatedSprite2D _animation;
    private Vector2I _animShift = new(0, -10);
    private bool _economyStarted;

    public CompanyCommon.CompanyType SignType { get; private set; } = CompanyCommon.CompanyType.Free;

    /// <summary>Set up as an economic company (catalogue row) before entering the tree.</summary>
    public void InitBusiness(OriginalBusinessTable.Entry entry, CityBlock block, Vector2I cell)
    {
        Entry = entry;
        Block = block;
        Cell = cell;
        CompanyName = RandomNames.Company();
        EnsureComponents(CompanyCommon.CompanyType.Free);
        _company.SetMaxHp(entry.MaxHP);
        UpdateSign();
    }

    /// <summary>Set up as a non-economic building (HQ placed on an empty start point…).</summary>
    public void InitBuilding(CompanyCommon.CompanyType signType, Vector2I cell)
    {
        Cell = cell;
        EnsureComponents(signType);
        SignType = signType;
    }

    /// <summary>Police station ('P' in a block, fnSetBuildingPolice): police cars start and end here.</summary>
    public bool IsPoliceStation { get; private set; }

    public void InitPoliceStation(Vector2I cell)
    {
        InitBuilding(CompanyCommon.CompanyType.NonInteractive, cell);
        IsPoliceStation = true;
        Selectable = false;
    }

    /// <summary>Turn this company into the player's headquarters.</summary>
    public void MakeHq(Team team)
    {
        IsHq = true;
        EnsureComponents(CompanyCommon.CompanyType.HQ);
        _company.SetMaxHp(HqMaxHp);
        _company.CanBeAttacked.HPisZERO += _ => OnHqDestroyed();
        OwnerTeam = team;
        UpdateOwnerRelation();
        UpdateSign();
    }

    /// <summary>HQ building (class 9, vtable off_477644) vt+F8h, sub_416750: 514h hit points.</summary>
    public const int HqMaxHp = 1300;

    /// <summary>
    /// HQ building vt+FCh (sub_415F20): "Your headquarter has been destroyed." (17,37) for the local owner, else
    /// "%s's headquater has been destroyed!" (17,38); the owner is eliminated (sub_43B677) and the building removed
    /// (sub_43BCE0). An HQ can't be captured in play, so the owner-change handler (vt+74h sub_4151C5) isn't ported.
    /// </summary>
    private void OnHqDestroyed()
    {
        if (!IsHq || OwnerTeam == null)
            return;
        var owner = OwnerTeam;
        GD.Print($"Company: HQ of {owner.PlayerName} destroyed");
        Messages.Show(owner.IsCurrentPlayerColor
            ? OriginalTexts.Instance.Get(17, 37)
            : PanelInfo.CFormat(OriginalTexts.Instance.Get(17, 38), owner.PlayerName));
        if (owner.HqCompany == this)
            owner.HqCompany = null;
        IsHq = false;
        OwnerTeam = null;
        owner.Org?.Eliminate();
        QueueFree();
    }

    private void EnsureComponents(CompanyCommon.CompanyType type)
    {
        if (_company != null)
            return;
        _company = new CompanyCommon(type);
        AddChild(_company);
    }

    public override void _Ready()
    {
        AddToGroup(GroupName);
        _animation = GetChild<AnimatedSprite2D>(0);
        // Scene sub-resources are shared between instances; give each company its own frames.
        _animation.SpriteFrames = (SpriteFrames)_animation.SpriteFrames.Duplicate();
        _selection = FindChild("Selection") as Selection;
        EnsureComponents(CompanyCommon.CompanyType.Free);
        // A beaten company doesn't change hands by itself: the next extortion attempt finds it below 15 % HP.
        Reinit();
    }

    /// <summary>Start the company's timed events (call once the clock runs).</summary>
    public void StartEconomy(GameClock clock)
    {
        if (_economyStarted || !IsEconomic)
            return;
        _economyStarted = true;
        clock.ScheduleIn(GameClock.MinutesPerHour, () => HourTick(clock));
        ScheduleIncomeTick(clock);
        clock.ScheduleIn(GameClock.MinutesPerWeek, () => WeekTick(clock));
    }

    // ---- timed events (sub_420ED5) -----------------------------------------------------------------------------------

    private void HourTick(GameClock clock)
    {
        if (!IsInstanceValid(this))
            return;
        CachedIncomeEnabled = Block?.IncomeEnabledFor(OwnerTeam) ?? true;
        if ((OwnerTeam == null || BusinessType == OriginalBusinessTable.BusinessType.Legal) && Hp < MaxHp)
            Heal(HealPerHour);
        clock.ScheduleIn(GameClock.MinutesPerHour, () => HourTick(clock));
    }

    private void ScheduleIncomeTick(GameClock clock)
    {
        // Next tick: start of the week + 22 × (tick + 1) + random(22), as the original does.
        const int slot = GameClock.MinutesPerWeek / IncomeTicksPerWeek; // 22
        var now = clock.Minute;
        var weekStart = now / GameClock.MinutesPerWeek * GameClock.MinutesPerWeek;
        var tick = now % GameClock.MinutesPerWeek / slot;
        var next = weekStart + slot * (tick + 1) + GD.RandRange(0, slot - 1);
        clock.ScheduleAt(next, () => IncomeTick(clock));
    }

    private void IncomeTick(GameClock clock)
    {
        if (!IsInstanceValid(this))
            return;
        if (Entry.Income > 0)
        {
            if ((Block?.IsActive ?? true) && !Frozen && CachedIncomeEnabled)
                Uncollected += Income() / IncomeTicksPerWeek;
        }
        else
        {
            // Support businesses cost their owner upkeep.
            OwnerTeam?.Org?.Spend(-Income() / IncomeTicksPerWeek);
        }
        ScheduleIncomeTick(clock);
    }

    private void WeekTick(GameClock clock)
    {
        if (!IsInstanceValid(this))
            return;
        Percent = 100;
        if (OwnerTeam == null)
            Uncollected = 0;
        clock.ScheduleIn(GameClock.MinutesPerWeek, () => WeekTick(clock));
    }

    // ---- income (sub_422AF4 / sub_422BC5 / sub_422E60 / sub_420E70) ------------------------------------------------

    /// <summary>Weekly income including the owner's type multiplier (vt+188h).</summary>
    public long Income()
    {
        var gross = GrossIncome();
        if (Entry == null || Entry.Income < 0 || OwnerTeam?.Org == null)
            return gross;
        return (long)(gross * OwnerTeam.Org.TypeFactor[(int)BusinessType]);
    }

    /// <summary>Weekly income without the owner's multiplier (vt+18Ch).</summary>
    public long GrossIncome()
    {
        if (Entry == null)
            return 0;
        if (Entry.Income < 0)
            return Entry.Income;
        var income = (long)(Percent / 100.0 * Entry.Income * DamageFactor());
        if (BusinessType == OriginalBusinessTable.BusinessType.Legal)
            income = (long)(income * DistrictFactor());
        return income;
    }

    /// <summary>1 at full HP, falling to 0 at 75 % HP (sub_422E60).</summary>
    public float DamageFactor()
    {
        var max = MaxHp;
        var hp = Hp;
        if (max <= 0 || hp >= max)
            return 1f;
        return hp > 0.75 * max ? (float)((hp - 0.75 * max) / (0.25 * max)) : 0f;
    }

    /// <summary>Legal companies earn ×1 / ×1.25 / ×1.875 in slum / middle-class / center blocks.</summary>
    public float DistrictFactor() =>
        CityBlock.DistrictIncomeFactor[Math.Clamp(Block?.District ?? 0, 0, CityBlock.DistrictIncomeFactor.Length - 1)];

    /// <summary>Collector pickup (vt+184h): returns the uncollected money and empties it.</summary>
    public long TakeUncollected()
    {
        var amount = Uncollected;
        Uncollected = 0;
        return amount;
    }

    // ---- health ------------------------------------------------------------------------------------------------------

    public int Hp => _company?.CanBeAttacked?.HP ?? 0;
    public int MaxHp => _company?.CanBeAttacked?.MaxHP ?? 0;

    public void Heal(int amount)
    {
        if (_company?.CanBeAttacked is { } cba)
            cba.HP = Math.Min(cba.MaxHP, cba.HP + amount);
    }

    // ---- extortion (sub_422FB4, base chance sub_423330, pressure sub_4232C1, cops sub_422F50) ---------------------

    /// <summary>Base chance by income: (income, chance %) points, linear in between (table at 0x47FCAC).</summary>
    private static readonly (long Income, int Chance)[] ExtortChanceTable =
        { (0, 100), (25_000, 90), (100_000, 15), (1_000_000, 0), (int.MaxValue, 0) };

    /// <summary>
    /// One extortion attempt by <paramref name="unit"/> (the unit's order calls this every game hour while it stands
    /// next to the company). Returns true when the company pays (it then belongs to the unit's player).
    /// </summary>
    public bool Extort(Unit unit, int attempt)
    {
        if (!IsEconomic || unit.OwnerTeam == null || unit.OwnerTeam == OwnerTeam)
            return true;

        var chance = ExtortBaseChance(unit) - (attempt > 0 ? 45 : 0) + (OwnerTeam != null ? 15 : 0);
        var hpRatio = MaxHp > 0 ? (float)Hp / MaxHp : 1f;

        if (OwnerTeam == null)
        {
            var copsThreshold = CopsThreshold(unit.UnitType);
            if (copsThreshold == attempt)
            {
                chance -= 100;
                CallCops(unit);
            }
            else if (copsThreshold < attempt)
                chance -= 100;
        }

        var pays = BusinessType == OriginalBusinessTable.BusinessType.Legal && GD.RandRange(0, 99) <= chance
                   || hpRatio < SurrenderHpRatio;
        if (pays)
        {
            GD.Print($"Company {KindName} '{CompanyName}' pays {unit.OwnerTeam.PlayerName} (attempt {attempt}, chance {chance}%)");
            Sound.Play(Sound.RandomOf(Sound.BusinessPay));
            SetOwnerTeam(unit.OwnerTeam);
            return true;
        }
        if (attempt == 0)
            Sound.Play(Sound.RandomOf(Sound.BusinessRefuse));
        GD.Print($"Company {KindName} '{CompanyName}' refuses (attempt {attempt}, chance {chance}%)");
        return false;
    }

    /// <summary>Base chance from income, plus/minus the pressure of units within 4 cells (own +, others −).</summary>
    private int ExtortBaseChance(Unit unit)
    {
        long income = Income();
        var chance = 0;
        for (var k = 0; k + 1 < ExtortChanceTable.Length; k++)
        {
            var (loIncome, loChance) = ExtortChanceTable[k];
            var (hiIncome, hiChance) = ExtortChanceTable[k + 1];
            if (income > hiIncome)
                continue;
            // chance = hi + (lo − hi) × (hiIncome − income) / (hiIncome − loIncome)
            chance = (int)(hiChance + (long)(loChance - hiChance) * (hiIncome - income) / (hiIncome - loIncome));
            break;
        }

        var tree = GetTree();
        foreach (var node in tree.GetNodesInGroup(Unit.GroupName))
        {
            if (node is not Unit other || other.IsDead || other.Map == null)
                continue;
            var cell = other.Map.LocalToMap(other.Position);
            if (Math.Abs(cell.X - Cell.X) > 4 || Math.Abs(cell.Y - Cell.Y) > 4)
                continue;
            var pressure = UnitPressure(other.UnitType);
            chance += other.OwnerTeam == unit.OwnerTeam ? pressure : -pressure;
        }
        return chance;
    }

    /// <summary>sub_4232C1: how much a unit type pushes a company to pay.</summary>
    public static int UnitPressure(UnitCommon.UnitType type) => type switch
    {
        UnitCommon.UnitType.Thug => 5,
        UnitCommon.UnitType.Pistol or UnitCommon.UnitType.Shotgun => 6,
        UnitCommon.UnitType.Thompson => 10,
        UnitCommon.UnitType.Terrorist or UnitCommon.UnitType.Hitman => 20,
        _ => 0,
    };

    /// <summary>sub_422F50: at which attempt the owner calls the cops (slum 10, middle 5, center 0; by unit type).</summary>
    public int CopsThreshold(UnitCommon.UnitType type)
    {
        int value = (Block?.District ?? 0) switch { 0 => 10, 1 => 5, _ => 0 };
        value += type switch
        {
            UnitCommon.UnitType.Thompson => 3,
            UnitCommon.UnitType.Terrorist or UnitCommon.UnitType.Hitman => 5,
            UnitCommon.UnitType.Sniper => -2,
            _ => 0,
        };
        return Math.Max(0, value);
    }

    private void CallCops(Unit unit)
    {
        // Original: a police object (sub_4460C5) comes for the gangster; message (30, 23) for the local player.
        if (unit.OwnerTeam?.IsCurrentPlayerColor == true)
            Messages.Show(string.Format(OriginalTexts.Instance.Get(30, 23).Replace("%s", "{0}"), CompanyName));
        Police.Instance?.Respond(this, unit);
    }

    // ---- business conversion (menu sub_421370, job sub_427050, SetRow sub_422070, sell sub_421E40 case 11) ----------

    /// <summary>Row being built by a running conversion job (company vt+9Ch busy), or null.</summary>
    public OriginalBusinessTable.Entry Building { get; private set; }
    /// <summary>Conversion job start / end minute (job +30h / +34h).</summary>
    public long BuildStart { get; private set; }
    public long BuildEnd { get; private set; }

    /// <summary>Job progress 0–1 for the panel bar (job vt+80h).</summary>
    public float BuildProgress(long now) => BuildEnd <= BuildStart ? 1f : Mathf.Clamp((float)(now - BuildStart) / (BuildEnd - BuildStart), 0f, 1f);
    public bool UnderConstruction => Building != null;

    public enum ConversionResult { Started, NotAllowed, NoMoney, NoFavour }

    /// <summary>
    /// Conversion job start (sub_427050 event 1): the owner must afford the price ("Not enough money to upgrade
    /// business.", 25,1) and pay the row's licence favour ("Not enough IOU points…", 25,0; support rows don't need
    /// it); then the price is paid and the business opens after the row's build time (catalogue +1Ch minutes).
    /// </summary>
    public ConversionResult StartConversion(OriginalBusinessTable.Entry row)
    {
        var org = OwnerTeam?.Org;
        if (org == null || !IsEconomic || UnderConstruction || row == null)
            return ConversionResult.NotAllowed;
        var local = OwnerTeam.IsCurrentPlayerColor;
        if (!org.CanAfford(row.Price))
        {
            if (local)
                Messages.Show(OriginalTexts.Instance.Get(25, 1));
            return ConversionResult.NoMoney;
        }
        var licence = BusinessRules.LicenceItem(row);
        if (licence >= 0 && !org.UseItem(licence) && row.Type != OriginalBusinessTable.BusinessType.Support)
        {
            if (local)
                Messages.Show(OriginalTexts.Instance.Get(25, 0));
            return ConversionResult.NoFavour;
        }
        org.Spend(row.Price);
        Building = row;
        BuildStart = GameClock.Instance?.Minute ?? 0;
        BuildEnd = BuildStart + row.Value1C;
        var owner = OwnerTeam;
        GD.Print($"Company {CompanyName}: {owner.PlayerName} builds {row.Name} for {row.Price} ({row.Value1C} min)");
        GameClock.Instance?.ScheduleIn(row.Value1C, () => FinishConversion(row, owner));
        return ConversionResult.Started;
    }

    /// <summary>Job event 3E8h: rows with flag +2Ch also need the politician (service 1Fh); then the row is set.</summary>
    private void FinishConversion(OriginalBusinessTable.Entry row, Team owner)
    {
        if (!IsInstanceValid(this) || Building != row)
            return;
        Building = null;
        if (row.Flag2C != 0 && owner.Org?.UseItem(0x1F) != true)
            return;
        SetRow(row);
    }

    /// <summary>
    /// sub_422070: becomes catalogue row <paramref name="row"/>. HP rise to the new maximum if they were full (or
    /// above it); another player opening a row with +20h = 2 is announced ("%s founds a %s.", 30,22).
    /// </summary>
    public void SetRow(OriginalBusinessTable.Entry row)
    {
        var health = _company?.CanBeAttacked;
        var wasFull = health == null || health.HP >= health.MaxHP;
        Entry = row;
        if (health != null)
        {
            health.MaxHP = row.MaxHP;
            if (wasFull || health.HP > row.MaxHP)
                health.HP = row.MaxHP;
        }
        UpdateSign();
        if (OwnerTeam != null && !OwnerTeam.IsCurrentPlayerColor && row.Value20 == 2)
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(30, 22), OwnerTeam.PlayerName, row.Name));
        GD.Print($"Company {CompanyName} is now {row}");
    }

    // ---- repair (menu action 1, job sub_4289A0 / sub_4289E8) -------------------------------------------------------

    public const int RepairHpPerStep = 5;
    public bool Repairing { get; private set; }
    /// <summary>A conversion or repair job runs (company vt+9Ch).</summary>
    public bool Busy => UnderConstruction || Repairing;

    /// <summary>vt+160h (sub_422F13): price / max HP × 0.5 per hit point; the HQ building repairs for free (sub_402C20).</summary>
    public long RepairCostPerHp => IsHq || Entry == null || MaxHp <= 0 ? 0 : (long)(Entry.Price / MaxHp * 0.5);

    /// <summary>Menu "Repair ($%ld)" (30,6): missing HP × cost per HP.</summary>
    public long RepairCost => (MaxHp - Hp) * RepairCostPerHp;

    public bool CanRepair => OwnerTeam != null && !Busy && Hp < MaxHp
                             && (IsHq || (IsEconomic && BusinessType != OriginalBusinessTable.BusinessType.Legal));

    /// <summary>
    /// Repair job: every game hour heal 5 HP and charge the owner one HP's cost (as the original does), until full;
    /// the panel bar runs to now + missing HP × 12 min.
    /// </summary>
    public void StartRepair()
    {
        if (!CanRepair)
            return;
        Repairing = true;
        BuildStart = GameClock.Instance?.Minute ?? 0;
        RepairStep();
    }

    private void RepairStep()
    {
        if (!IsInstanceValid(this) || !Repairing)
            return;
        var clock = GameClock.Instance;
        if (Hp >= MaxHp || OwnerTeam?.Org == null || clock == null)
        {
            Repairing = false;
            return;
        }
        BuildEnd = clock.Minute + (MaxHp - Hp) * GameClock.MinutesPerHour / RepairHpPerStep;
        Heal(RepairHpPerStep);
        OwnerTeam.Org.Spend(RepairCostPerHp);
        clock.ScheduleIn(GameClock.MinutesPerHour, RepairStep);
    }

    public long SellPrice => Entry == null ? 0 : (long)(Entry.Price * BusinessRules.SellFactor);

    /// <summary>Menu "Sell for $%ld" (30,7): an undamaged business of the owner.</summary>
    public bool CanSell => IsEconomic && OwnerTeam != null && !Busy
                           && BusinessType != OriginalBusinessTable.BusinessType.Legal && Hp >= MaxHp;

    /// <summary>
    /// sub_421E40 case 11: the owner gets half the price (Deposit), the building becomes a random legal company of its
    /// grade for its district (fnSelectBuildingRandomly) and belongs to nobody.
    /// </summary>
    public void Sell()
    {
        if (!CanSell)
            return;
        OwnerTeam.Org?.Deposit(SellPrice);
        var legal = OriginalBusinessTable.Instance.RandomLegal(Entry.Grade, Block?.District ?? OriginalBusinessTable.AnyDistrict);
        if (legal != null)
            SetRow(legal);
        SetOwnerTeam(null);
    }

    /// <summary>
    /// The owner was eliminated: like a sale without the money, a business becomes a random legal company of its grade
    /// for its district and the company belongs to nobody; a running conversion or repair is dropped.
    /// </summary>
    public void ReleaseToNeutral()
    {
        if (!IsEconomic || OwnerTeam == null)
            return;
        Building = null;
        Repairing = false;
        if (BusinessType != OriginalBusinessTable.BusinessType.Legal)
        {
            var legal = OriginalBusinessTable.Instance.RandomLegal(Entry.Grade, Block?.District ?? OriginalBusinessTable.AnyDistrict);
            if (legal != null)
                SetRow(legal);
        }
        Uncollected = 0;
        SetOwnerTeam(null);
    }

    // ---- ownership (vt+74h, sub_4211F0) ------------------------------------------------------------------------------

    /// <summary>Change owner; a neutral company loses its uncollected cash, and the block registers the new owner.</summary>
    public void SetOwnerTeam(Team team)
    {
        if (OwnerTeam == team)
            return;
        if (OwnerTeam == null)
            Uncollected = 0;
        OwnerTeam = team;
        UpdateOwnerRelation();
        UpdateSign();
        Block?.OnCompanyAcquired(this);
    }

    private void UpdateOwnerRelation()
    {
        var current = Teams.GetCurrentPlayerTeam();
        CompanyOwner = OwnerTeam == null || current == null
            ? CompanyCommon.CompanyOwner.Neutral
            : current.GetTargetRelation(OwnerTeam.TeamColor) switch
            {
                Teams.TeamRelation.Self => CompanyCommon.CompanyOwner.Player,
                Teams.TeamRelation.Friend => CompanyCommon.CompanyOwner.Teammate,
                _ => CompanyCommon.CompanyOwner.Enemy,
            };
    }

    /// <summary>Sign type from catalogue type and ownership (see TODO "company logos").</summary>
    private void UpdateSign()
    {
        SignType = IsHq ? CompanyCommon.CompanyType.HQ
            : Entry == null ? SignType
            : Entry.Type switch
            {
                OriginalBusinessTable.BusinessType.Legal => OwnerTeam == null
                    ? CompanyCommon.CompanyType.Free : CompanyCommon.CompanyType.Occupied,
                OriginalBusinessTable.BusinessType.Bootlegging => CompanyCommon.CompanyType.Bootlegging,
                OriginalBusinessTable.BusinessType.Entertainment => CompanyCommon.CompanyType.Entertainment,
                OriginalBusinessTable.BusinessType.Gambling => CompanyCommon.CompanyType.Gambling,
                OriginalBusinessTable.BusinessType.Troubleshooting => CompanyCommon.CompanyType.Troubleshooting,
                OriginalBusinessTable.BusinessType.Support => Entry.Value28 == 3
                    ? CompanyCommon.CompanyType.SupportMoney : CompanyCommon.CompanyType.SupportFire,
                _ => CompanyCommon.CompanyType.Occupied,
            };
        if (InFog)
        {
            _signPending = true; // out of the player's sight: keep showing the last seen sign
            return;
        }
        ApplySign();
    }

    private void ApplySign()
    {
        _signPending = false;
        _company?.SetSignType(SignType);
        _animShift = SignType == CompanyCommon.CompanyType.Free ? new Vector2I(0, -10) : new Vector2I(0, -30);
        Reinit();
    }

    private bool _signPending;
    private bool _inFog;
    private Team _seenOwner;
    private OriginalBusinessTable.Entry _seenEntry;

    /// <summary>Owner as the local player last saw it (the current one when in sight).</summary>
    public Team ShownOwner => _inFog ? _seenOwner : OwnerTeam;

    /// <summary>Catalogue row as the local player last saw it.</summary>
    public OriginalBusinessTable.Entry ShownEntry => _inFog ? _seenEntry : Entry;

    /// <summary>
    /// Out of the local player's sight (<see cref="FogOfWar"/>): the sign keeps its last seen owner and type until the
    /// company is seen again (user, from the original game).
    /// </summary>
    public bool InFog
    {
        get => _inFog;
        set
        {
            if (_inFog == value)
                return;
            if (value)
            {
                _seenOwner = OwnerTeam;
                _seenEntry = Entry;
            }
            _inFog = value;
            if (!value && _signPending)
                ApplySign();
        }
    }

    public void Reinit()
    {
        if (_animation == null || _company == null)
            return;
        _animation.Stop();
        _animation.Position = _animShift;
        if (!_company.IsInteractive)
            return;
        _company.FillSpriteFrames(_animation.SpriteFrames);
        var animName = CompanyCommon.SpriteFrameNamesDict[CompanyOwner];
        if (_animation.SpriteFrames.HasAnimation(animName))
            _animation.Play(animName);
    }

    public override string ToString() =>
        $"{KindName} '{CompanyName}' ({BusinessType}, owner {OwnerTeam?.PlayerName ?? "none"}, HP {Hp}/{MaxHp}, income {Income()}, uncollected {Uncollected})";
}
