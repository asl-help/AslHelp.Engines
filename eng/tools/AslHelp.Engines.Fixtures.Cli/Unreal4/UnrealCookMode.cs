namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Specifies whether a package step cooks the content.
/// </summary>
internal enum UnrealCookMode
{
    /// <summary>
    ///     The step cooks the content, with <c>-cook</c>.
    /// </summary>
    Cook,

    /// <summary>
    ///     The step reuses the content that an earlier step cooked, with <c>-skipcook</c>.
    /// </summary>
    SkipCook,
}
