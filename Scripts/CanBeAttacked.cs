using Godot;

namespace Capone;

/// <summary>Health component with evasion. Emits HPisZERO with the attacker's team colour.</summary>
public partial class CanBeAttacked : Node2D, IJsonConfigurable
{
    public const string JsonKey = "can_be_attacked";

    [Signal] public delegate void HPisZEROEventHandler(Color attackerColor);

    [Export] public int MaxHP { get; set; } = 15;
    public int HP { get; set; } = 1;
    public int HealRate { get; set; } = 1;
    [Export] public int EvasionFactor { get; set; } = 5;
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
            case "EvasionFactor": EvasionFactor = JsonUtil.ToInt(value); break;
            case "HealRate": HealRate = JsonUtil.ToInt(value); break;
            case "DisplayHP": DisplayHP = value.AsBool(); break;
            default: GD.PushWarning($"CanBeAttacked: unknown json key '{key}'"); break;
        }
    }

    public void ReceiveDamage(int damage, CanAttack attacker)
    {
        var crit = false;
        if (EvasionFactor > 0)
        {
            // damage = 5, evasion = 5 => about 50 % to evade
            if ((int)(GD.Randi() % (uint)EvasionFactor) * 2 + 1 > damage)
            {
                // Evaded, unless the 1 % "lucky hit" happens.
                if (GD.Randi() % 100 + 1 < 99)
                    return;
                crit = true; // a lucky hit can't also be a lucky miss
            }

            if (!crit && GD.Randi() % 100 + 1 < 2)
                return; // 1 % lucky miss
        }

        HP = Mathf.Max(0, HP - damage);
        if (HP == 0)
        {
            var attackerEntity = attacker?.GetParent()?.GetParent() as IGameEntity;
            if (attackerEntity != null)
                EmitSignal(SignalName.HPisZERO, attackerEntity.TeamColor);
            if (attacker != null)
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
