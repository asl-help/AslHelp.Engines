namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides 1 method for a <see cref="ValueAccess{T}"/> with its value type and 1 for any other access,
///     so a collection creates the typed reads of its elements without reflection.
/// </summary>
/// <typeparam name="TResult">The type of the result of a visit.</typeparam>
internal abstract class AccessVisitor<TResult>
{
    /// <summary>
    ///     Visits an access whose kind doesn't read a value, like a struct.
    /// </summary>
    /// <param name="access">The access.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public abstract TResult VisitOther(PropertyAccess access);

    /// <summary>
    ///     Visits an access whose kind reads a value of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="access">The access.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public abstract TResult VisitValue<T>(ValueAccess<T> access);
}
