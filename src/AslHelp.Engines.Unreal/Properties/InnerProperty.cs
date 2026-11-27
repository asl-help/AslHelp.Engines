using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides the read of a property that a collection property points at, like the property of the elements of an array.
/// </summary>
internal static class InnerProperty
{
    /// <summary>
    ///     Reads the property that the pointer at <paramref name="offset"/> in <paramref name="collectionProperty"/> points at.
    /// </summary>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="collectionProperty">The collection property.</param>
    /// <param name="member">The member with the pointer, like <c>Inner</c>, for the failure.</param>
    /// <param name="offset">The offset of the pointer in <paramref name="collectionProperty"/>.</param>
    /// <returns>
    ///     The property or a failure when the pointer is 0 or a read fails.
    /// </returns>
    public static Result<UnrealProperty> Read(ClassReader classReader, UnrealProperty collectionProperty, string member, int offset)
    {
        var address = collectionProperty.Address + offset;

        if (!classReader.Memory.ReadPointer(address).TryUnwrap(out var inner, out var error))
        {
            return CollectionError.MemberUnreadable(collectionProperty.Name, member, address, error);
        }

        return inner == Address.Zero
            ? CollectionError.MemberNull(collectionProperty.Name, member, collectionProperty.Address, offset)
            : classReader.ReadProperty(inner);
    }
}
