namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Represents 1 member of the fake data object and the value a chain watcher gives for it.
/// </summary>
/// <param name="Member">The member after <c>GWorld.GameMode.Data</c>, like <c>Coord.X</c>.</param>
/// <param name="Expected">The value in the type of the kind.</param>
public readonly record struct KindCase(
    string Member,
    object Expected);
