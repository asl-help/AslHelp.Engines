using System.Globalization;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents an <c>FVector</c>, with the floats before 5.0 widened to the doubles of 5.0 and later.
/// </summary>
/// <param name="X">The X component.</param>
/// <param name="Y">The Y component.</param>
/// <param name="Z">The Z component.</param>
public readonly record struct Vector(
    double X,
    double Y,
    double Z)
{
    /// <summary>
    ///     Returns the components like <c>(1.25, -2.5, 3.75)</c>, with the invariant culture so a log line reads the same everywhere.
    /// </summary>
    /// <returns>
    ///     The text.
    /// </returns>
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }
}
