namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides the largest values that a read takes, so a damaged count fails instead of reading megabytes on every tick.
/// </summary>
internal static class ValueBounds
{
    /// <summary>
    ///     The most characters of an <c>FString</c> that a read takes.
    /// </summary>
    public const int MaxChars = 512;

    /// <summary>
    ///     The most elements of an array that a whole read takes and the most slots of a set or map that a whole read
    ///     or a key lookup takes.
    /// </summary>
    /// <remarks>
    ///     1 element of an array by index only checks the index against the count. The slots of a set or map are its elements
    ///     and the free slots that removed elements left.
    /// </remarks>
    public const int MaxElements = 1024;
}
