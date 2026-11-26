namespace AslHelp.Engines.Unreal;

/// <summary>
///     Specifies the kinds of value that <see cref="UnrealProperty"/> reads.
/// </summary>
internal enum PropertyKind
{
    /// <summary>
    ///     A kind the library doesn't read.
    /// </summary>
    Unsupported,

    /// <summary>
    ///     A 4-byte signed integer.
    /// </summary>
    Int32,

    /// <summary>
    ///     A pointer to an object.
    /// </summary>
    Object,
}
