using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed package lookup of <see cref="UnrealGame"/>.
/// </summary>
internal abstract record PackageError : ResultError
{
    /// <summary>
    ///     Creates the error for a class that a package doesn't have.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="name">The name of the class.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PackageError ClassNotFound(string packageName, string name)
    {
        return new ClassNotFoundError(packageName, name);
    }

    /// <summary>
    ///     Creates the error for a package that isn't loaded.
    /// </summary>
    /// <param name="name">The name of the package.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PackageError NotLoaded(string name)
    {
        return new NotLoadedError(name);
    }

    /// <summary>
    ///     Creates the error for a search of the object array that failed on a read.
    /// </summary>
    /// <param name="name">The name of the package.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PackageError SearchFailed(string name, ResultError cause)
    {
        return new SearchFailedError(name, cause);
    }
}

file sealed record ClassNotFoundError(
    string PackageName,
    string Name) : PackageError
{
    public override string Message => SR.FormatPackageClassNotFound(PackageName, Name);
}

file sealed record NotLoadedError(
    string Name) : PackageError
{
    public override string Message => SR.FormatPackageNotLoaded(Name);
}

file sealed record SearchFailedError(
    string Name,
    ResultError Cause) : PackageError
{
    public override string Message => SR.FormatPackageSearchFailed(Name, Cause.Message);
}
