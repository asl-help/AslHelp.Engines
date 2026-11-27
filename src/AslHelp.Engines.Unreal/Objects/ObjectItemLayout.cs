namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Represents the offsets inside 1 item of an object array.
/// </summary>
/// <param name="ObjectPointer">The offset of the object pointer.</param>
/// <param name="Size">The size of 1 item in bytes, which is how far apart the items sit.</param>
/// <param name="SerialNumber">
///     The offset of the 4-byte serial number that a weak pointer compares or <see langword="null"/> when the items don't have one.
/// </param>
public sealed record ObjectItemLayout(
    int ObjectPointer,
    int Size,
    int? SerialNumber = null);
