using System;
using System.Linq;
using System.Reflection;
using Godot;

namespace Capone.Tests;

/// <summary>Marks a static method without parameters as a unit test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnitTestAttribute : Attribute { }

public sealed class TestFailure : Exception
{
    public TestFailure(string message) : base(message) { }
}

public static class Assert
{
    public static void True(bool condition, string what)
    {
        if (!condition)
            throw new TestFailure(what);
    }

    public static void False(bool condition, string what) => True(!condition, what);

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!Equals(expected, actual))
            throw new TestFailure($"{what}: expected {expected}, got {actual}");
    }

    public static void Near(double expected, double actual, string what, double tolerance = 1e-4)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new TestFailure($"{what}: expected {expected}, got {actual}");
    }
}

/// <summary>
/// Unit tests run inside Godot (PLAN M13): most rules read the original EXE or use Godot types, which need the engine,
/// so a separate xUnit project would first need the data readers rewritten. Every static method marked
/// <see cref="UnitTestAttribute"/> in this assembly is run.
/// <para>Run: <c>Godot --headless --path . -- --unit-test[=filter]</c>; exit code 0 = all passed.</para>
/// </summary>
public static class UnitTests
{
    public static int Run(string filter = null)
    {
        var tests = typeof(UnitTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.GetCustomAttribute<UnitTestAttribute>() != null && m.GetParameters().Length == 0)
            .Where(m => string.IsNullOrEmpty(filter) || $"{m.DeclaringType?.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType?.Name).ThenBy(m => m.Name)
            .ToList();
        GD.Print($"=== Unit tests: {tests.Count} ===");
        var failures = 0;
        foreach (var test in tests)
        {
            var name = $"{test.DeclaringType?.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
                GD.Print($"  ok   {name}");
            }
            catch (TargetInvocationException e)
            {
                failures++;
                var inner = e.InnerException;
                GD.PrintErr(inner is TestFailure
                    ? $"  FAIL {name}: {inner.Message}"
                    : $"  FAIL {name}: {inner}");
            }
        }
        GD.Print($"=== Unit tests done: {tests.Count - failures} passed, {failures} failure(s) ===");
        return failures;
    }
}
