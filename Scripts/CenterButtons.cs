using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>The six action buttons under the avatar. Handlers are stubs for now.</summary>
public partial class CenterButtons : Node
{
    private readonly List<TextureButton> _buttons = new();

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is not TextureButton button)
                continue;
            var number = _buttons.Count + 1;
            _buttons.Add(button);
            button.Pressed += () => ButtonPressed(number);
        }
    }

    /// <summary>number is 1-based, left to right.</summary>
    private void ButtonPressed(int number)
    {
        // TODO: actions depend on the selected object (see plan.md, 3.4)
    }
}
