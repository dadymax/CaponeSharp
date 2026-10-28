using Godot;

namespace Capone;

/// <summary>Health component (object +20h HP, sub_438710). Emits HPisZERO with the attacker's team colour.</summary>
public partial class CanBeAttacked : Node2D, IJsonConfigurable
{
    public const string JsonKey = "can_be_attacked";

    [Signal] public delegate void HPisZEROEventHandler(Color attackerColor);

    [Export] public int MaxHP { get; set; } = 15;
    public int HP { get; set; } = 1;
    /// <summary>The unit that hit last (the killer, when HP reached 0).</summary>
    public Unit LastAttacker { get; private set; }
    public int HealRate { get; set; } = 1;
    [Export] public bool DisplayHP { get; set; } = true;

    public Rect2I Rect { get; private set; }
    private Vector2[] _points;
    private Color _color;
    private float _healTimer;

    public void ApplyJsonValue(string key, Variant value)
    {
        switch (key)
        {
            case "maxHP": MaxHP = JsonUtil.ToInt(value); break;
            case "EvasionFactor": break; // not in the original: hits are decided by the shot (sub_438873)
            case "HealRate": HealRate = JsonUtil.ToInt(value); break;
            case "DisplayHP": DisplayHP = value.AsBool(); break;
            default: GD.PushWarning($"CanBeAttacked: unknown json key '{key}'"); break;
        }
    }

    public void ReceiveDamage(int damage, CanAttack attacker)
    {
        // Both sides count as fighting for the next game hour (unit vt+11Ch).
        (GetParent()?.GetParent() as Unit)?.MarkFighting();
        if (attacker != null && IsInstanceValid(attacker))
            (attacker.GetParent()?.GetParent() as Unit)?.MarkFighting();
        // sub_42BC00: a unit that is hit loses its aim.
        (GetParent()?.GetParent() as Unit)?.CanAttack?.OnHit();
        // sub_438710: the HP change itself — no evasion (whether a bullet hits is decided by the shot, sub_438873).
        if (HP <= 0)
            return;
        HP = Mathf.Max(0, HP - damage);
        if (HP == 0)
        {
            // Destroyed even when the attacker is unknown or already gone (a bomb, a test hit).
            var known = attacker != null && IsInstanceValid(attacker);
            var attackerEntity = known ? attacker.GetParent()?.GetParent() as IGameEntity : null;
            LastAttacker = attackerEntity as Unit;
            EmitSignal(SignalName.HPisZERO, attackerEntity?.TeamColor ?? Teams.NeutralTeamColor);
            if (known)
                attacker.Target = null;
        }
    }

    public void Heal(double delta)
    {
        if (HP >= MaxHP)
            return;
        _healTimer -= (float)delta;
        if (_healTimer <= 0)
        {
            HP = Mathf.Min(MaxHP, HP + 1);
            _healTimer = HealRate;
        }
    }

    public float GetPercent() => HP * 1.0f / MaxHP;

    public override void _Ready()
    {
        var parent = GetParent()?.GetParent();
        if (parent != null)
        {
            var shapes = parent.FindChildren("*", nameof(CollisionShape2D), false);
            if (shapes.Count > 0 && shapes[0] is CollisionShape2D cs && cs.Shape != null)
            {
                var rect = cs.Shape.GetRect();
                Rect = new Rect2I((Vector2I)rect.Position, (Vector2I)rect.Size);
            }
        }

        _points = new[]
        {
            new Vector2(Rect.Position.X, Rect.Position.Y),
            new Vector2(Rect.Position.X + Rect.Size.X, Rect.Position.Y),
            new Vector2(Rect.Position.X + Rect.Size.X, Rect.Position.Y + 2),
            new Vector2(Rect.Position.X, Rect.Position.Y + 2),
        };
        _color = Colors.Green;
        HP = MaxHP;
        _healTimer = HealRate;
    }
}
