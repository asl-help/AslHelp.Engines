namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a game whose engine version or key <see cref="UnrealTarget.Detect"/> can't find.
/// </summary>
internal abstract record TargetError : ResultError
{
    /// <summary>
    ///     Creates the error for an Unreal Engine 3 stamp whose build isn't a known UDK release.
    /// </summary>
    /// <param name="build">The engine build of the stamp.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError BuildUnknown(int build)
    {
        return new BuildUnknownError(build);
    }

    /// <summary>
    ///     Creates the error for a pointer size that isn't x86 or x64.
    /// </summary>
    /// <param name="pointerSize">The pointer size of the memory.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError PointerSizeUnsupported(byte pointerSize)
    {
        return new PointerSizeUnsupportedError(pointerSize);
    }

    /// <summary>
    ///     Creates the error for a module file without an engine version in its version resource, like a game that stamps its own version.
    /// </summary>
    /// <param name="module">The name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError StampMissing(string module)
    {
        return new StampMissingError(module);
    }

    /// <summary>
    ///     Creates the error for a passed version that isn't an Unreal Engine version.
    /// </summary>
    /// <param name="version">The text of the version.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError VersionInvalid(string version)
    {
        return new VersionInvalidError(version);
    }
}

file sealed record BuildUnknownError(
    int Build) : TargetError
{
    public override string Message => SR.FormatTargetBuildUnknown(Build);
}

file sealed record PointerSizeUnsupportedError(
    byte PointerSize) : TargetError
{
    public override string Message => SR.FormatTargetPointerSizeUnsupported(PointerSize);
}

file sealed record StampMissingError(
    string Module) : TargetError
{
    public override string Message => SR.FormatTargetStampMissing(Module);
}

file sealed record VersionInvalidError(
    string Version) : TargetError
{
    public override string Message => SR.FormatTargetVersionInvalid(Version);
}
