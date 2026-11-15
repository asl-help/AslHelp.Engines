using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Specifies where the runtime DLLs of the players of an engine come from.
/// </summary>
internal enum UnrealRuntimeSource
{
    /// <summary>
    ///     The build image, for engines without <c>AppLocalDependencies</c>.
    /// </summary>
    [JsonStringEnumMemberName("image")]
    Image,

    /// <summary>
    ///     The <c>Engine/Binaries/ThirdParty/AppLocalDependencies</c> folder of the engine.
    /// </summary>
    [JsonStringEnumMemberName("engine")]
    Engine,
}
