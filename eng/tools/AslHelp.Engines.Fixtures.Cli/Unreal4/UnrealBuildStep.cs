using AslHelp.Engines.Fixtures.Unreal4;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Describes 1 step of a build. The log file and the messages of the step use its name.
/// </summary>
internal closed record UnrealBuildStep
{
    /// <summary>
    ///     Gets the name of the step, such as <c>editor</c> or <c>package-win-x64-shipping</c>.
    /// </summary>
    public string Name => this switch
    {
        Editor => "editor",
        Runtimes => "runtimes",
        Package package => $"package-{package.Player.Tag}",
    };

    /// <summary>
    ///     Describes the step that builds the editor target.
    /// </summary>
    internal sealed record Editor : UnrealBuildStep;

    /// <summary>
    ///     Describes the step that copies the runtime DLLs out of the image.
    /// </summary>
    internal sealed record Runtimes : UnrealBuildStep;

    /// <summary>
    ///     Describes the step that packages a player.
    /// </summary>
    /// <param name="Player">The player.</param>
    internal sealed record Package(UnrealPlayer Player) : UnrealBuildStep;
}
