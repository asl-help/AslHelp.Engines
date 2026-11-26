using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed class or property lookup of <see cref="UnrealGame"/>.
/// </summary>
internal abstract record ClassError : ResultError
{
    /// <summary>
    ///     Creates the error for a chain of class pointers that doesn't end at <c>Class</c>, whose class is itself.
    /// </summary>
    /// <param name="obj">The address of the object the chain starts from.</param>
    /// <param name="offset">The offset of the class pointer in an object.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ClassError MetaclassNotFound(Address obj, int offset)
    {
        return new MetaclassNotFoundError(obj, offset);
    }

    /// <summary>
    ///     Creates the error for a name that no loaded class has.
    /// </summary>
    /// <param name="name">The name of the class.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ClassError NotFound(string name)
    {
        return new NotFoundError(name);
    }

    /// <summary>
    ///     Creates the error for an object whose class pointer is 0.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="offset">The offset of the class pointer in an object.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ClassError Null(Address obj, int offset)
    {
        return new NullError(obj, offset);
    }

    /// <summary>
    ///     Creates the error for a name that no class on the super chain of a class declares as a property.
    /// </summary>
    /// <param name="className">The name of the class.</param>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ClassError PropertyNotFound(string className, string name)
    {
        return new PropertyNotFoundError(className, name);
    }

    /// <summary>
    ///     Creates the error for a search of the object array that failed on a read.
    /// </summary>
    /// <param name="name">The name of the class.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ClassError SearchFailed(string name, ResultError cause)
    {
        return new SearchFailedError(name, cause);
    }
}

file sealed record MetaclassNotFoundError(
    Address Object,
    int Offset) : ClassError
{
    public override string Message => SR.FormatClassMetaclassNotFound(Object, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record NotFoundError(
    string Name) : ClassError
{
    public override string Message => SR.FormatClassNotFound(Name);
}

file sealed record NullError(
    Address Object,
    int Offset) : ClassError
{
    public override string Message => SR.FormatClassNull(Object, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record PropertyNotFoundError(
    string ClassName,
    string Name) : ClassError
{
    public override string Message => SR.FormatClassPropertyNotFound(ClassName, Name);
}

file sealed record SearchFailedError(
    string Name,
    ResultError Cause) : ClassError
{
    public override string Message => SR.FormatClassSearchFailed(Name, Cause.Message);
}
