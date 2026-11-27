namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides 1 method per shape of <see cref="UnrealProperty"/>, which <see cref="UnrealProperty.Accept"/> calls.
/// </summary>
/// <remarks>
///     A new value kind goes to <see cref="VisitValue"/> with its type, so it doesn't add a method.
///     A new shape adds a virtual method that calls <see cref="VisitUnsupported"/>, so a visitor that doesn't override it keeps working.
/// </remarks>
/// <typeparam name="TResult">The type of the result of a visit.</typeparam>
public abstract class PropertyVisitor<TResult>
{
    /// <summary>
    ///     Visits a property of an array, a set or a map, which a path indexes and which reads as a whole when its elements are values.
    /// </summary>
    /// <remarks>
    ///     Calls <see cref="VisitUnsupported"/> unless a visitor overrides it.
    /// </remarks>
    /// <param name="prop">
    ///     The property, with <c>ArrayProperty</c>, <c>SetProperty</c> or <c>MapProperty</c> in <see cref="UnrealProperty.ClassName"/>.
    /// </param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public virtual TResult VisitCollection(UnrealProperty prop)
    {
        return VisitUnsupported(prop);
    }

    /// <summary>
    ///     Visits a property of an enum, whose value reads as the number of its underlying property, like a byte.
    /// </summary>
    /// <remarks>
    ///     Calls <see cref="VisitUnsupported"/> unless a visitor overrides it.
    /// </remarks>
    /// <param name="prop">The property.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public virtual TResult VisitEnum(UnrealProperty prop)
    {
        return VisitUnsupported(prop);
    }

    /// <summary>
    ///     Visits a property of a struct, which a path goes into and which reads as an array when its members are numbers of 1 class.
    /// </summary>
    /// <remarks>
    ///     Calls <see cref="VisitUnsupported"/> unless a visitor overrides it.
    /// </remarks>
    /// <param name="prop">The property.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public virtual TResult VisitStruct(UnrealProperty prop)
    {
        return VisitUnsupported(prop);
    }

    /// <summary>
    ///     Visits a property of a kind the library doesn't read, like <c>TextProperty</c>.
    /// </summary>
    /// <param name="prop">The property, with the kind in <see cref="UnrealProperty.ClassName"/>.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public abstract TResult VisitUnsupported(UnrealProperty prop);

    /// <summary>
    ///     Visits a property whose value <see cref="UnrealGame.Read{T}"/> reads as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the value: <see cref="sbyte"/>, <see cref="short"/>, <see cref="int"/>, <see cref="long"/>, <see cref="byte"/>,
    ///     <see cref="ushort"/>, <see cref="uint"/>, <see cref="ulong"/>, <see cref="float"/>, <see cref="double"/>, <see cref="bool"/>,
    ///     <see cref="string"/> or <see cref="Memory.Address"/> for an object pointer or a weak pointer.
    /// </typeparam>
    /// <param name="prop">The property.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public abstract TResult VisitValue<T>(UnrealProperty prop);
}
