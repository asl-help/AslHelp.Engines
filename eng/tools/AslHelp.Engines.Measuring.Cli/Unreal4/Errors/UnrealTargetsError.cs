namespace AslHelp.Engines.Measuring.Cli.Unreal4.Errors;

/// <summary>
///     Represents a list of Unreal targets that breaks a rule of the Unreal measurement.
/// </summary>
internal sealed record UnrealTargetsError : ResultError
{
    private UnrealTargetsError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a target or a member with a range that doesn't read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the target or <c>Type.Member</c>.</param>
    /// <param name="from">The start of the range.</param>
    /// <param name="until">The end of the range.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealTargetsError TargetsBadRange(string path, string name, string? from, string? until)
    {
        return new(UnrealSR.FormatTargetsBadRange(path, name, from, until));
    }

    /// <summary>
    ///     Creates the error for a constant without a scope.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the constant.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealTargetsError TargetsConstantUnscoped(string path, string name)
    {
        return new(UnrealSR.FormatTargetsConstantUnscoped(path, name));
    }

    /// <summary>
    ///     Creates the error for a global whose constructed range leaves its range.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the global.</param>
    /// <param name="global">The target of the global.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealTargetsError TargetsConstructedOutside(string path, string name, UnrealGlobalTarget global)
    {
        return new(UnrealSR.FormatTargetsConstructedOutside(path, name, global.ConstructedFrom, global.ConstructedUntil, global.From, global.Until));
    }

    /// <summary>
    ///     Creates the error for a member called like the size of its type.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the member as <c>Type.Member</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealTargetsError TargetsMemberReserved(string path, string name)
    {
        return new(UnrealSR.FormatTargetsMemberReserved(path, name));
    }

    /// <summary>
    ///     Creates the error for a constant whose report key is also a member or the size of a layout.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the constant.</param>
    /// <param name="key">The report key, such as <c>Scope.Value</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealTargetsError TargetsReportKeyTaken(string path, string name, string key)
    {
        return new(UnrealSR.FormatTargetsReportKeyTaken(path, name, key));
    }
}
