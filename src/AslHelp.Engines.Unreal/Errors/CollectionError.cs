using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed read of an array, a set or a map or of 1 of its elements.
/// </summary>
internal abstract record CollectionError : ResultError
{
    /// <summary>
    ///     Gets a value indicating whether the key can't match any key of the collection, like a text key on an array.
    /// </summary>
    public virtual bool IsKeyTypeMismatch => false;

    /// <summary>
    ///     Creates the error for a collection whose count is below 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="count">The count.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError CountNegative(string property, int count)
    {
        return new CountNegativeError(property, count);
    }

    /// <summary>
    ///     Creates the error for a count after a member that isn't a collection.
    /// </summary>
    /// <param name="member">The name of the member.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError CountNotCollection(string member)
    {
        return new CountNotCollectionError(member);
    }

    /// <summary>
    ///     Creates the error for a collection with elements whose data pointer is 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="count">The count.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError DataNull(string property, int count)
    {
        return new DataNullError(property, count);
    }

    /// <summary>
    ///     Creates the error for a collection whose element size isn't above 0, like a set layout from a wrong offset.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="size">The size of 1 element in bytes.</param>
    /// <param name="member">The member that the size comes from, like <c>SetLayout</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError ElementSizeInvalid(string property, int size, string member)
    {
        return new ElementSizeInvalidError(property, size, member);
    }

    /// <summary>
    ///     Creates the error for a read of a whole collection whose elements don't read as values, like an array of structs.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the property, like <c>ArrayProperty</c>.</param>
    /// <param name="elementClass">The class of the elements, like <c>StructProperty</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError ElementsNotValue(string property, string className, string elementClass)
    {
        return new ElementsNotValueError(property, className, elementClass);
    }

    /// <summary>
    ///     Creates the error for a set or a map whose number of free slots is below 0 or above its number of slots.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="free">The number of free slots.</param>
    /// <param name="slots">The number of slots.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError FreeSlotsOutOfRange(string property, int free, int slots)
    {
        return new FreeSlotsOutOfRangeError(property, free, slots);
    }

    /// <summary>
    ///     Creates the error for an index outside the elements of an array.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="index">The index.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError IndexOutOfRange(string property, int index, int count)
    {
        return new IndexOutOfRangeError(property, index, count);
    }

    /// <summary>
    ///     Creates the error for a key that a map doesn't have.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="key">The text of the key, like <c>"two"</c> or <c>5</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError KeyNotFound(string property, string key)
    {
        return new KeyNotFoundError(property, key);
    }

    /// <summary>
    ///     Creates the error for a set or a map whose keys don't read as values, like a struct key.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="keyClass">The class of the keys, like <c>StructProperty</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError KeyNotValue(string property, string keyClass)
    {
        return new KeyNotValueError(property, keyClass);
    }

    /// <summary>
    ///     Creates the error for a key whose type can't match the keys of a collection, like a text key on an array.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="keyType">The name of the type of the keys, like <c>Int32</c>.</param>
    /// <param name="key">The text of the key.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError KeyTypeMismatch(string property, string keyType, string key)
    {
        return new KeyTypeMismatchError(property, keyType, key);
    }

    /// <summary>
    ///     Creates the error for a member after a collection.
    /// </summary>
    /// <param name="property">The name of the collection.</param>
    /// <param name="member">The name of the member.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError MemberAfterCollection(string property, string member)
    {
        return new MemberAfterCollectionError(property, member);
    }

    /// <summary>
    ///     Creates the error for a collection property whose pointer to an inner property is 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="member">The member with the pointer, like <c>Inner</c>.</param>
    /// <param name="address">The address of the property.</param>
    /// <param name="offset">The offset of the member in the profile.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError MemberNull(string property, string member, Address address, int offset)
    {
        return new MemberNullError(property, member, address, offset);
    }

    /// <summary>
    ///     Creates the error for a member of a collection property that didn't read, like the pointer to its inner property.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="member">The member, like <c>Inner</c>.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError MemberUnreadable(string property, string member, Address address, ResultError cause)
    {
        return new MemberUnreadableError(property, member, address, cause);
    }

    /// <summary>
    ///     Creates the error for an index after a member that isn't a collection.
    /// </summary>
    /// <param name="member">The name of the member.</param>
    /// <param name="key">The text of the index or key.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError NotCollection(string member, string key)
    {
        return new NotCollectionError(member, key);
    }

    /// <summary>
    ///     Creates the error for an array with more elements than a whole read takes.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="count">The number of elements.</param>
    /// <param name="maxCount">The most elements a read takes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError TooLong(string property, int count, int maxCount)
    {
        return new TooLongError(property, count, maxCount);
    }

    /// <summary>
    ///     Creates the error for a set or map with more slots, used or free, than a whole read or a key lookup takes.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="slots">The number of slots.</param>
    /// <param name="maxSlots">The most slots a read takes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CollectionError TooManySlots(string property, int slots, int maxSlots)
    {
        return new TooManySlotsError(property, slots, maxSlots);
    }
}

file sealed record CountNegativeError(
    string Property,
    int Count) : CollectionError
{
    public override string Message => SR.FormatCollectionCountNegative(Property, Count);
}

file sealed record CountNotCollectionError(
    string Member) : CollectionError
{
    public override string Message => SR.FormatCollectionCountNotCollection(Member);
}

file sealed record DataNullError(
    string Property,
    int Count) : CollectionError
{
    public override string Message => SR.FormatCollectionDataNull(Property, Count);
}

file sealed record ElementSizeInvalidError(
    string Property,
    int Size,
    string Member) : CollectionError
{
    public override string Message => SR.FormatCollectionElementSizeInvalid(Property, Size, Member);
}

file sealed record ElementsNotValueError(
    string Property,
    string ClassName,
    string ElementClass) : CollectionError
{
    public override string Message => SR.FormatCollectionElementsNotValue(Property, ClassName, ElementClass);
}

file sealed record FreeSlotsOutOfRangeError(
    string Property,
    int Free,
    int Slots) : CollectionError
{
    public override string Message => SR.FormatCollectionFreeSlotsOutOfRange(Property, Free, Slots);
}

file sealed record IndexOutOfRangeError(
    string Property,
    int Index,
    int Count) : CollectionError
{
    public override string Message => SR.FormatCollectionIndexOutOfRange(Property, Index, Count);
}

file sealed record KeyNotFoundError(
    string Property,
    string Key) : CollectionError
{
    public override string Message => SR.FormatCollectionKeyNotFound(Property, Key);
}

file sealed record KeyNotValueError(
    string Property,
    string KeyClass) : CollectionError
{
    public override string Message => SR.FormatCollectionKeyNotValue(Property, KeyClass);
}

file sealed record KeyTypeMismatchError(
    string Property,
    string KeyType,
    string Key) : CollectionError
{
    public override bool IsKeyTypeMismatch => true;

    public override string Message => SR.FormatCollectionKeyTypeMismatch(Property, KeyType, Key);
}

file sealed record MemberAfterCollectionError(
    string Property,
    string Member) : CollectionError
{
    public override string Message => SR.FormatCollectionMemberAfterCollection(Property, Member);
}

file sealed record MemberNullError(
    string Property,
    string Member,
    Address Address,
    int Offset) : CollectionError
{
    public override string Message
        => SR.FormatCollectionMemberNull(Property, Member, Address, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record MemberUnreadableError(
    string Property,
    string Member,
    Address Address,
    ResultError Cause) : CollectionError
{
    public override string Message => SR.FormatCollectionMemberUnreadable(Property, Member, Address, Cause.Message);
}

file sealed record NotCollectionError(
    string Member,
    string Key) : CollectionError
{
    public override string Message => SR.FormatCollectionNotCollection(Member, Key);
}

file sealed record TooLongError(
    string Property,
    int Count,
    int MaxCount) : CollectionError
{
    public override string Message => SR.FormatCollectionTooLong(Property, Count, MaxCount);
}

file sealed record TooManySlotsError(
    string Property,
    int Slots,
    int MaxSlots) : CollectionError
{
    public override string Message => SR.FormatCollectionTooManySlots(Property, Slots, MaxSlots);
}
