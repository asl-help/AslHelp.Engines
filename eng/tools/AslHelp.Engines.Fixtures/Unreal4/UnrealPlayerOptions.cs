using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Fixtures.Unreal4;

/// <summary>
///     Creates the options that pick players by a part of their tag.
/// </summary>
/// <remarks>
///     Each option takes a part of the tag in lowercase, such as <c>x64</c>. The help lists every value.
/// </remarks>
public static class UnrealPlayerOptions
{
    /// <summary>
    ///     Creates the <c>--arch</c> option, which takes <c>x64</c> or <c>x86</c>.
    /// </summary>
    /// <param name="description">The description of the option in the help.</param>
    /// <returns>
    ///     The option. Its value is <see langword="null"/> when the command line leaves it out.
    /// </returns>
    public static Option<Architecture?> Arch(string description)
    {
        Option<Architecture?> option = new("--arch")
        {
            Description = description,
            CustomParser = ParseArch,
        };

        option.CompletionSources.Clear();
        option.CompletionSources.Add(UnrealPlayer.ArchName(Architecture.X64), UnrealPlayer.ArchName(Architecture.X86));

        return option;
    }

    /// <summary>
    ///     Creates the <c>--config</c> option, which takes <c>development</c> or <c>shipping</c>.
    /// </summary>
    /// <param name="description">The description of the option in the help.</param>
    /// <returns>
    ///     The option. Its value is <see langword="null"/> when the command line leaves it out.
    /// </returns>
    public static Option<UnrealConfig?> Config(string description)
    {
        Option<UnrealConfig?> option = new("--config")
        {
            Description = description,
            CustomParser = ParseConfig,
        };

        option.CompletionSources.Clear();
        option.CompletionSources.Add(UnrealPlayer.ConfigName(UnrealConfig.Development), UnrealPlayer.ConfigName(UnrealConfig.Shipping));

        return option;
    }

    private static Architecture? ParseArch(ArgumentResult result)
    {
        // Leave a missing or 2nd value to the arity check, which fails the parse with its own error.
        if (result.Tokens is not [var token])
        {
            return null;
        }

        if (UnrealPlayer.TryParseArch(token.Value, out var arch))
        {
            return arch;
        }

        result.AddError(SR.FormatPlayerArchNameInvalid(token.Value));

        return null;
    }

    private static UnrealConfig? ParseConfig(ArgumentResult result)
    {
        // Leave a missing or 2nd value to the arity check, which fails the parse with its own error.
        if (result.Tokens is not [var token])
        {
            return null;
        }

        if (UnrealPlayer.TryParseConfig(token.Value, out var config))
        {
            return config;
        }

        result.AddError(SR.FormatPlayerConfigNameInvalid(token.Value));

        return null;
    }
}
