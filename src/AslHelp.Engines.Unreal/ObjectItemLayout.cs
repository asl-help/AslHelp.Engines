namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside 1 item of an object array.
/// </summary>
/// <param name="ObjectPointer">The offset of the object pointer.</param>
/// <param name="Size">The size of 1 item in bytes, which is how far apart the items sit.</param>
public sealed record ObjectItemLayout(
    int ObjectPointer,
    int Size);
