using AslHelp.Engines.Fixtures.Cli.Hosting;
using System.Collections.Generic;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Describes the host folders of 1 build and where the build steps see them.
/// </summary>
/// <param name="Engine">The engine folder.</param>
/// <param name="Scratch">The folder of the build, with the project, the archives, the logs and the players.</param>
/// <param name="Steps">The folder of the step scripts.</param>
internal sealed record UnrealPaths(
    string Engine,
    string Scratch,
    string Steps)
{
    /// <summary>
    ///     Represents where a step sees <see cref="Engine"/>.
    /// </summary>
    public const string EngineMount = @"C:\e";

    /// <summary>
    ///     Represents where a step sees <see cref="Logs"/>.
    /// </summary>
    public const string LogsMount = @"C:\l";

    /// <summary>
    ///     Represents where a step sees <see cref="Out"/>.
    /// </summary>
    public const string OutMount = @"C:\o";

    /// <summary>
    ///     Represents where a step sees <see cref="Project"/>.
    /// </summary>
    public const string ProjectMount = @"C:\p";

    /// <summary>
    ///     Represents the end of the name of a scratch folder, after the toolchain and the version.
    /// </summary>
    public const string ScratchSuffix = "-build";

    /// <summary>
    ///     Represents where a step sees <see cref="Steps"/>.
    /// </summary>
    public const string StepsMount = @"C:\r";

    /// <summary>
    ///     Gets the rendered project.
    /// </summary>
    public string Project => Path.Combine(Scratch, "project");

    /// <summary>
    ///     Gets the folder the package steps archive into, 1 subfolder per player tag.
    /// </summary>
    public string Out => Path.Combine(Scratch, "out");

    /// <summary>
    ///     Gets the folder of the step logs.
    /// </summary>
    public string Logs => Path.Combine(Scratch, "logs");

    /// <summary>
    ///     Gets the folder of the finished players, 1 subfolder per player tag.
    /// </summary>
    public string Players => Path.Combine(Scratch, "players");

    /// <summary>
    ///     Gets the mounts of every step: the engine at <c>C:\e</c>, the project at <c>C:\p</c>, the archives at <c>C:\o</c>, the
    ///     logs at <c>C:\l</c> and the scripts read-only at <c>C:\r</c>.
    /// </summary>
    /// <remarks>
    ///     The engine is mounted read-write, because the build tools write into <c>Engine/Intermediate</c> and <c>Engine/Programs</c>.
    /// </remarks>
    public IReadOnlyList<BuildMount> Mounts
    {
        get
        {
            return
            [
                new(Engine, EngineMount, ReadOnly: false),
                new(Project, ProjectMount, ReadOnly: false),
                new(Out, OutMount, ReadOnly: false),
                new(Logs, LogsMount, ReadOnly: false),
                new(Steps, StepsMount, ReadOnly: true),
            ];
        }
    }

    /// <summary>
    ///     Gets the path of the log of a step.
    /// </summary>
    /// <param name="buildStep">The step, such as <see cref="UnrealBuildStep.Editor"/>.</param>
    /// <returns>
    ///     The path of <c>&lt;step&gt;.log</c> in <see cref="Logs"/>.
    /// </returns>
    public string LogPath(UnrealBuildStep buildStep)
    {
        return Path.Combine(Logs, $"{buildStep.Name}.log");
    }
}
