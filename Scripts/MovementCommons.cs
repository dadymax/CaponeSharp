namespace Capone;

public static class MovementCommons
{
    public enum MovementDirection { SE, NE, NW, SW }
    public enum UnitAction { idle, attack, move }

    public static string GetAnimLayerName(UnitAction action, MovementDirection direction)
    {
        var result = action switch
        {
            UnitAction.idle => "Idle",
            UnitAction.attack => "Attack",
            UnitAction.move => "Move",
            _ => "",
        };
        return result + direction;
    }
}
