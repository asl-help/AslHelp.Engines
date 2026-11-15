namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside the field class of a property that isn't an object.
/// </summary>
/// <param name="Name">The offset of the name id, with the 4-byte name number right after it.</param>
public sealed record FieldClassLayout(
    int Name);
