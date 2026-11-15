namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Describes a host folder that a <see cref="BuildStep"/> sees at a path in its container.
/// </summary>
/// <param name="HostPath">The full path of the folder on the host.</param>
/// <param name="ContainerPath">The path in the container, such as <c>C:\e</c>.</param>
/// <param name="ReadOnly">Whether the step can only read the folder.</param>
internal sealed record BuildMount(
    string HostPath,
    string ContainerPath,
    bool ReadOnly);
