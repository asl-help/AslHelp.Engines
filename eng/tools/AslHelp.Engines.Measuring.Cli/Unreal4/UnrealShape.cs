namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Specifies 1 layout of a type that moved across engine versions.
/// </summary>
/// <remarks>
///     <see cref="UnrealShapes.NameOf"/> gives the spelling of <c>$shape</c> in the engine library profiles.
/// </remarks>
internal enum UnrealShape
{
    /// <summary>
    ///     Properties as <c>UProperty</c> objects, <c>uproperty</c>.
    /// </summary>
    UProperty,

    /// <summary>
    ///     Properties as <c>FField</c> structs, <c>ffield</c>.
    /// </summary>
    FField,

    /// <summary>
    ///     A table that reaches its entries through pointers, <c>indirect</c>.
    /// </summary>
    Indirect,

    /// <summary>
    ///     A table in chunks, <c>chunked</c>.
    /// </summary>
    Chunked,

    /// <summary>
    ///     A table of object pointers, <c>pointers</c>.
    /// </summary>
    Pointers,

    /// <summary>
    ///     A table of object items in 1 array, <c>flat</c>.
    /// </summary>
    Flat,
}
