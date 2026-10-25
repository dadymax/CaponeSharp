using Godot;

namespace Capone;

/// <summary>A company building on the map. Its data and components live in a CompanyCommon child.</summary>
public partial class Company : StaticBody2D, ISelectable, IGameEntity
{
    public const string GroupName = "companies";

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

    public CanAttack CanAttack => _company?.CanAttack;
    public CanBeAttacked CanBeAttacked => _company?.CanBeAttacked;
    public CanHoldMoney CanHoldMoney => _company?.CanHoldMoney;
    public CompanyEconomics CompanyEconomics => _company?.CompanyEconomics;

    private Color _teamColor = Teams.NeutralTeamColor;
    public Color TeamColor
    {
        get => _teamColor;
        set
        {
            _teamColor = value;
            ApplyOwnerFromRelation(value);
            Reinit();
        }
    }

    private CompanyCommon.CompanyType _companyType;
    public CompanyCommon.CompanyType CompanyType
    {
        get => _companyType;
        private set
        {
            _companyType = value;
            _animShift = value == CompanyCommon.CompanyType.Free ? new Vector2I(0, -10) : new Vector2I(0, -30);
            if (_animation != null)
                _animation.Position = _animShift;
        }
    }

    private CompanyCommon.CompanyOwner _companyOwner;
    private CompanyCommon _company;
    private CompanyCommon.CompanyBldngGrade _companyGrade;
    private int _companySubType;
    private CompanyResistance _resistance;
    private Vector2I _animShift = new(0, -10);
    private AnimatedSprite2D _animation;

    public CompanyCommon CompanyData => _company;
    public CompanyCommon.CompanyOwner CompanyOwner => _companyOwner;
    public CompanyCommon.CompanyBldngGrade CompanyGrade => _companyGrade;
    public int CompanySubType => _companySubType;

    public void SetType(CompanyCommon.CompanyType type)
    {
        if (_company != null)
        {
            if (_company.CanBeAttacked != null)
                _company.CanBeAttacked.HPisZERO -= OnHPisZero;
            RemoveChild(_company);
            _company.QueueFree();
        }
        _company = new CompanyCommon(type);
        AddChild(_company);
        if (IsNodeReady() && _company.CanBeAttacked != null)
            _company.CanBeAttacked.HPisZERO += OnHPisZero;
        CompanyType = type;
        Reinit();
    }

    public void SetOwner(CompanyCommon.CompanyOwner owner)
    {
        _companyOwner = owner;
        if (owner == CompanyCommon.CompanyOwner.Neutral && _resistance == null)
        {
            _resistance = new CompanyResistance();
            AddChild(_resistance);
            _resistance.ResistanceBroken += OnResistanceBroken;
        }
    }

    public void SetGrade(CompanyCommon.CompanyBldngGrade grade) => _companyGrade = grade;

    public void SetSubtype(int subtype) => _companySubType = subtype;

    public override void _Ready()
    {
        AddToGroup(GroupName);
        _animation = GetChild<AnimatedSprite2D>(0);
        // Scene sub-resources are shared between instances; give each company its own frames.
        _animation.SpriteFrames = (SpriteFrames)_animation.SpriteFrames.Duplicate();
        _selection = FindChild("Selection") as Selection;
        if (_company?.CanBeAttacked != null)
            _company.CanBeAttacked.HPisZERO += OnHPisZero;
        Reinit();
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
        var animName = CompanyCommon.SpriteFrameNamesDict[_companyOwner];
        if (_animation.SpriteFrames.HasAnimation(animName)) // non-interactive companies have no animations
            _animation.Play(animName);
    }

    private void OnHPisZero(Color attackerColor)
    {
        TeamColor = attackerColor;
        SetSubtype(1);
        if (_companyType == CompanyCommon.CompanyType.Free)
            SetType(CompanyCommon.CompanyType.Occupied);
        ApplyOwnerFromRelation(attackerColor);
        Reinit();
    }

    /// <summary>
    /// Capture by an attacking unit. Used for neutral companies, where the resistance system applies.
    /// attackerParent is the attacking Unit (or Company).
    /// </summary>
    public void CaptureBy(Color attackerColor, Node2D attackerParent)
    {
        if (_companyOwner != CompanyCommon.CompanyOwner.Neutral)
            return;

        var attackerCost = attackerParent is Unit unit ? unit.UnitData?.Cost ?? 0 : 0;

        if (_resistance != null && _resistance.ResistAttack(attackerParent, attackerCost))
        {
            // Resistance held: the company takes a token hit.
            CanBeAttacked?.ReceiveDamage(1, null);
            return;
        }

        TeamColor = attackerColor;
        SetSubtype(1);
        if (_companyType == CompanyCommon.CompanyType.Free)
            SetType(CompanyCommon.CompanyType.Occupied);
        ApplyOwnerFromRelation(attackerColor);
        _resistance?.ResetResistance();
        Reinit();
    }

    private void ApplyOwnerFromRelation(Color color)
    {
        var playerTeam = Teams.GetCurrentPlayerTeam();
        if (playerTeam == null)
            return;
        switch (playerTeam.GetTargetRelation(color))
        {
            case Teams.TeamRelation.Self: SetOwner(CompanyCommon.CompanyOwner.Player); break;
            case Teams.TeamRelation.Friend: SetOwner(CompanyCommon.CompanyOwner.Teammate); break;
            case Teams.TeamRelation.Enemy: SetOwner(CompanyCommon.CompanyOwner.Enemy); break;
        }
    }

    public override void _Process(double delta) => CompanyEconomics?.Process(delta);

    private void OnResistanceBroken()
    {
        GD.Print("[Company] Resistance broken, captured automatically.");
        Reinit();
    }
}
