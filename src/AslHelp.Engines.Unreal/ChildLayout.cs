namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside a field on the children chain of a struct, which is how Unreal Engine 3 and Unreal Engine 4 through 4.24 store fields.
/// </summary>
/// <param name="Next">The offset of the pointer to the next field.</param>
public sealed record ChildLayout(
    int Next);
