namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides 1 method per kind of <see cref="UnrealProperty"/>, which <see cref="UnrealProperty.Accept"/> calls.
/// </summary>
/// <typeparam name="TResult">The type of the result of a visit.</typeparam>
public interface IPropertyVisitor<out TResult>
{
    /// <summary>
    ///     Visits a property with a 4-byte signed integer value, which <see cref="UnrealGame.ReadInt32"/> reads.
    /// </summary>
    /// <param name="prop">The property.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    TResult VisitInt32(UnrealProperty prop);

    /// <summary>
    ///     Visits a property with an object pointer value, which <see cref="UnrealGame.ReadObject"/> reads.
    /// </summary>
    /// <param name="prop">The property.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    TResult VisitObject(UnrealProperty prop);

    /// <summary>
    ///     Visits a property of a kind the library doesn't read, like <c>FloatProperty</c>.
    /// </summary>
    /// <param name="prop">The property, with the kind in <see cref="UnrealProperty.ClassName"/>.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    TResult VisitUnsupported(UnrealProperty prop);
}
