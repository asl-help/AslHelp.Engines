using System.Collections.Generic;

namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Describes 1 build step, a command in a container of a build image with host folders mounted.
/// </summary>
/// <param name="Image">The image with its tag.</param>
/// <param name="Command">The command, 1 item per argument, such as <c>cmd</c>, <c>/c</c> and the command line.</param>
/// <param name="Mounts">The host folders the command reads and writes.</param>
/// <param name="Environment">The variables the command gets on top of the ones of the image.</param>
/// <param name="Owner">
///     The folder of the build that the step belongs to. It labels the container, so <see cref="IBuildHost.RemoveStepsAsync"/> finds a
///     container that a killed run left.
/// </param>
internal sealed record BuildStep(
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<BuildMount> Mounts,
    IReadOnlyDictionary<string, string> Environment,
    string Owner);
