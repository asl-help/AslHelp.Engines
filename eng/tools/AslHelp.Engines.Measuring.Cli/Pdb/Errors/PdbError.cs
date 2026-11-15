using System;

namespace AslHelp.Engines.Measuring.Cli.Pdb.Errors;

/// <summary>
///     Represents a failed lookup in a PDB or a failed read of a PDB or an executable.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a measurer that probes for optional types fails lookups and doesn't read the messages.
/// </remarks>
internal abstract record PdbError : ResultError
{
    /// <summary>
    ///     Creates the error for a constant name that finds 2 different values.
    /// </summary>
    /// <param name="name">The name of the constant.</param>
    /// <param name="first">The 1st value.</param>
    /// <param name="other">The other value.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError ConstantAmbiguous(string name, long first, long other)
    {
        return new ConstantAmbiguousError(name, first, other);
    }

    /// <summary>
    ///     Creates the error for a constant whose value doesn't fit a <see cref="long"/>, such as a float.
    /// </summary>
    /// <param name="name">The name of the constant.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError ConstantDoesNotFit(string name)
    {
        return new ConstantDoesNotFitError(name);
    }

    /// <summary>
    ///     Creates the error for a constant name that does not find a constant.
    /// </summary>
    /// <param name="name">The name of the constant.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError ConstantNotFound(string name)
    {
        return new ConstantNotFoundError(name);
    }

    /// <summary>
    ///     Creates the error for a PE file with a debug directory or a CodeView entry that does not read.
    /// </summary>
    /// <param name="path">The path of the executable.</param>
    /// <param name="reason">The reason the read failed.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError ExecutableCodeViewInvalid(string path, string reason)
    {
        return new ExecutableCodeViewInvalidError(path, reason);
    }

    /// <summary>
    ///     Creates the error for an executable without a CodeView debug entry.
    /// </summary>
    /// <param name="path">The path of the executable.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError ExecutableNoCodeView(string path)
    {
        return new ExecutableNoCodeViewError(path);
    }

    /// <summary>
    ///     Creates the error for a PDB of another build than the executable.
    /// </summary>
    /// <param name="pdbGuid">The GUID of the PDB.</param>
    /// <param name="pdbAge">The age of the PDB.</param>
    /// <param name="executableGuid">The GUID in the executable.</param>
    /// <param name="executableAge">The age in the executable.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError IdentityMismatch(Guid pdbGuid, int pdbAge, Guid executableGuid, int executableAge)
    {
        return new IdentityMismatchError(pdbGuid, pdbAge, executableGuid, executableAge);
    }

    /// <summary>
    ///     Creates the error for a PDB without a valid section header stream.
    /// </summary>
    /// <param name="path">The path of the PDB.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError NoSectionHeaders(string path)
    {
        return new NoSectionHeadersError(path);
    }

    /// <summary>
    ///     Creates the error for a file that is not a valid PDB.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The reason the read failed.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError NotPdb(string path, string reason)
    {
        return new NotPdbError(path, reason);
    }

    /// <summary>
    ///     Creates the error for a PDB with OMAP streams.
    /// </summary>
    /// <param name="path">The path of the PDB.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError OmapUnsupported(string path)
    {
        return new OmapUnsupportedError(path);
    }

    /// <summary>
    ///     Creates the error for a symbol text that finds 2 different RVAs.
    /// </summary>
    /// <param name="text">The text of the lookup.</param>
    /// <param name="firstRva">The 1st RVA.</param>
    /// <param name="otherRva">The other RVA.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError SymbolAmbiguous(string text, uint firstRva, uint otherRva)
    {
        return new SymbolAmbiguousError(text, firstRva, otherRva);
    }

    /// <summary>
    ///     Creates the error for a symbol text that does not find a symbol.
    /// </summary>
    /// <param name="text">The text of the lookup.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError SymbolNotFound(string text)
    {
        return new SymbolNotFoundError(text);
    }

    /// <summary>
    ///     Creates the error for a type with forward references and without a complete record.
    /// </summary>
    /// <param name="name">The name of the type.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError TypeForwardOnly(string name)
    {
        return new TypeForwardOnlyError(name);
    }

    /// <summary>
    ///     Creates the error for a type without any records.
    /// </summary>
    /// <param name="name">The name of the type.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError TypeNotFound(string name)
    {
        return new TypeNotFoundError(name);
    }

    /// <summary>
    ///     Creates the error for a type whose records did not read.
    /// </summary>
    /// <param name="name">The name of the type.</param>
    /// <param name="reason">The reason the read failed.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PdbError TypeUnreadable(string name, string reason)
    {
        return new TypeUnreadableError(name, reason);
    }
}

/// <summary>
///     Represents a constant name that finds 2 different values.
/// </summary>
/// <param name="Name">The name of the constant.</param>
/// <param name="First">The 1st value.</param>
/// <param name="Other">The other value.</param>
file sealed record ConstantAmbiguousError(
    string Name,
    long First,
    long Other) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbConstantAmbiguous(Name, First, Other);
}

/// <summary>
///     Represents a constant whose value doesn't fit a <see cref="long"/>, such as a float.
/// </summary>
/// <param name="Name">The name of the constant.</param>
file sealed record ConstantDoesNotFitError(
    string Name) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbConstantDoesNotFit(Name);
}

/// <summary>
///     Represents a constant name that does not find a constant.
/// </summary>
/// <param name="Name">The name of the constant.</param>
file sealed record ConstantNotFoundError(
    string Name) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbConstantNotFound(Name);
}

/// <summary>
///     Represents a PE file with a debug directory or a CodeView entry that does not read.
/// </summary>
/// <param name="Path">The path of the executable.</param>
/// <param name="Reason">The reason the read failed.</param>
file sealed record ExecutableCodeViewInvalidError(
    string Path,
    string Reason) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatExecutableCodeViewInvalid(Path, Reason);
}

/// <summary>
///     Represents an executable without a CodeView debug entry.
/// </summary>
/// <param name="Path">The path of the executable.</param>
file sealed record ExecutableNoCodeViewError(
    string Path) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatExecutableNoCodeView(Path);
}

/// <summary>
///     Represents a PDB of another build than the executable.
/// </summary>
/// <param name="PdbGuid">The GUID of the PDB.</param>
/// <param name="PdbAge">The age of the PDB.</param>
/// <param name="ExecutableGuid">The GUID in the executable.</param>
/// <param name="ExecutableAge">The age in the executable.</param>
file sealed record IdentityMismatchError(
    Guid PdbGuid,
    int PdbAge,
    Guid ExecutableGuid,
    int ExecutableAge) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbIdentityMismatch(PdbGuid, PdbAge, ExecutableGuid, ExecutableAge);
}

/// <summary>
///     Represents a PDB without a valid section header stream.
/// </summary>
/// <param name="Path">The path of the PDB.</param>
file sealed record NoSectionHeadersError(
    string Path) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbSectionHeadersMissing(Path);
}

/// <summary>
///     Represents a file that is not a valid PDB.
/// </summary>
/// <param name="Path">The path of the file.</param>
/// <param name="Reason">The reason the read failed.</param>
file sealed record NotPdbError(
    string Path,
    string Reason) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbInvalid(Path, Reason);
}

/// <summary>
///     Represents a PDB with OMAP streams.
/// </summary>
/// <param name="Path">The path of the PDB.</param>
file sealed record OmapUnsupportedError(
    string Path) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbOmapUnsupported(Path);
}

/// <summary>
///     Represents a symbol text that finds 2 different RVAs.
/// </summary>
/// <param name="Text">The text of the lookup.</param>
/// <param name="FirstRva">The 1st RVA.</param>
/// <param name="OtherRva">The other RVA.</param>
file sealed record SymbolAmbiguousError(
    string Text,
    uint FirstRva,
    uint OtherRva) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbSymbolAmbiguous(Text, $"{FirstRva:X}", $"{OtherRva:X}");
}

/// <summary>
///     Represents a symbol text that does not find a symbol.
/// </summary>
/// <param name="Text">The text of the lookup.</param>
file sealed record SymbolNotFoundError(
    string Text) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbSymbolNotFound(Text);
}

/// <summary>
///     Represents a type with forward references and without a complete record.
/// </summary>
/// <param name="Name">The name of the type.</param>
file sealed record TypeForwardOnlyError(
    string Name) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbTypeForwardOnly(Name);
}

/// <summary>
///     Represents a type without any records.
/// </summary>
/// <param name="Name">The name of the type.</param>
file sealed record TypeNotFoundError(
    string Name) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbTypeNotFound(Name);
}

/// <summary>
///     Represents a type whose records did not read.
/// </summary>
/// <param name="Name">The name of the type.</param>
/// <param name="Reason">The reason the read failed.</param>
file sealed record TypeUnreadableError(
    string Name,
    string Reason) : PdbError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPdbTypeUnreadable(Name, Reason);
}
