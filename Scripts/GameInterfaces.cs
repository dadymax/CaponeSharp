using System.Globalization;
using Godot;

namespace Capone;

/// <summary>Something the player can click to select (units and companies).</summary>
public interface ISelectable
{
    bool Selectable { get; }
    bool Selected { get; set; }
}

/// <summary>A map object that belongs to a team and is built from the component nodes.</summary>
public interface IGameEntity
{
    Color TeamColor { get; }
    CanAttack CanAttack { get; }
    CanBeAttacked CanBeAttacked { get; }
    CanHoldMoney CanHoldMoney { get; }
}

/// <summary>A component whose fields can be filled from a units.json / companies.json record.</summary>
public interface IJsonConfigurable
{
    void ApplyJsonValue(string key, Variant value);
}

public static class JsonUtil
{
    public static Godot.Collections.Dictionary LoadDictionary(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("JsonUtil: failed to open ", path);
            return new Godot.Collections.Dictionary();
        }
        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            GD.PrintErr($"JsonUtil: {path}:{json.GetErrorLine()}: {json.GetErrorMessage()}");
            return new Godot.Collections.Dictionary();
        }
        return json.Data.AsGodotDictionary();
    }

    /// <summary>Int from a JSON number or numeric string ("5").</summary>
    public static int ToInt(Variant v) => v.VariantType switch
    {
        Variant.Type.String => int.TryParse(v.AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0,
        Variant.Type.Float => (int)v.AsDouble(),
        Variant.Type.Int => v.AsInt32(),
        Variant.Type.Bool => v.AsBool() ? 1 : 0,
        _ => 0,
    };

    public static float ToFloat(Variant v) => v.VariantType switch
    {
        Variant.Type.String => float.TryParse(v.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f,
        Variant.Type.Float or Variant.Type.Int => (float)v.AsDouble(),
        _ => 0f,
    };

    /// <summary>
    /// Applies a JSON component record. "false" means the component is absent (returns null and frees it),
    /// "true" keeps defaults, an object sets fields.
    /// </summary>
    public static T FillFromJson<T>(T component, Variant json) where T : Node, IJsonConfigurable
    {
        if (json.VariantType == Variant.Type.Bool)
        {
            if (json.AsBool())
                return component;
            component.Free();
            return null;
        }
        foreach (var (key, value) in json.AsGodotDictionary())
            component.ApplyJsonValue(key.AsString(), value);
        return component;
    }
}
