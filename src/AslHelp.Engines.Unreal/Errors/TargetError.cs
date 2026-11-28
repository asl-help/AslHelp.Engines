namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a game whose engine version or key <see cref="UnrealTarget.Detect"/> can't find.
/// </summary>
internal abstract record TargetError : ResultError
{
    /// <summary>
    ///     Creates the error for a module file that doesn't open or read.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="reason">The message of the failed read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError ModuleFileUnreadable(string path, string reason)
    {
        return new ModuleFileUnreadableError(path, reason);
    }

    /// <summary>
    ///     Creates the error for a module whose path Windows didn't return, so its file version can't be read.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError ModulePathUnknown(string module)
    {
        return new ModulePathUnknownError(module);
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
    ///     Creates the error for a passed UDK release whose engine build isn't known, because no player of it is measured.
    /// </summary>
    /// <param name="release">The text of the release.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetError ReleaseUnmeasured(string release)
    {
        return new ReleaseUnmeasuredError(release);
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

file sealed record ModuleFileUnreadableError(
    string Path,
    string Reason) : TargetError
{
    public override string Message => SR.FormatTargetModuleFileUnreadable(Path, Reason);
}

file sealed record ModulePathUnknownError(
    string Module) : TargetError
{
    public override string Message => SR.FormatTargetModulePathUnknown(Module);
}

file sealed record PointerSizeUnsupportedError(
    byte PointerSize) : TargetError
{
    public override string Message => SR.FormatTargetPointerSizeUnsupported(PointerSize);
}

file sealed record ReleaseUnmeasuredError(
    string Release) : TargetError
{
    public override string Message => SR.FormatTargetReleaseUnmeasured(Release);
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
