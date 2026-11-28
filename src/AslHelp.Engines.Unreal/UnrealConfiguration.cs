namespace AslHelp.Engines.Unreal;

/// <summary>
///     Specifies the build configuration of a game, which is part of the key of a profile document.
/// </summary>
public enum UnrealConfiguration
{
    /// <summary>
    ///     A shipping or test build, <c>shipping</c> in the key. Released games are shipping builds.
    /// </summary>
    Shipping,

    /// <summary>
    ///     A development, debug game or debug build, <c>development</c> in the key.
    /// </summary>
    Development,
}
