using System.Globalization;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents an <c>FRotator</c> in degrees, with the floats before 5.0 widened to the doubles of 5.0 and later.
/// </summary>
/// <param name="Pitch">The rotation around the right axis.</param>
/// <param name="Yaw">The rotation around the up axis.</param>
/// <param name="Roll">The rotation around the forward axis.</param>
public readonly record struct Rotator(
    double Pitch,
    double Yaw,
    double Roll)
{
    /// <summary>
    ///     Returns the components like <c>(10, 20, 30)</c>, with the invariant culture so a log line reads the same everywhere.
    /// </summary>
    /// <returns>
    ///     The text.
    /// </returns>
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", Pitch, Yaw, Roll);
    }
}
