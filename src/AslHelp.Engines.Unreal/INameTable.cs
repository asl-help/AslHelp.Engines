namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of names from 1 storage shape of the name table.
/// </summary>
public interface INameTable
{
    /// <summary>
    ///     Reads the name with <paramref name="id"/>.
    /// </summary>
    /// <remarks>
    ///     A failed read of an id can succeed later, because the engine adds entries while it runs.
    /// </remarks>
    /// <param name="id">The id of the name, in the form an object stores it.</param>
    /// <returns>
    ///     The text of the name or a failure with the id and the failed read.
    /// </returns>
    Result<string> Read(int id);
}
