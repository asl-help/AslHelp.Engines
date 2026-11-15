namespace AslHelp.Engines.Measuring.Cli.Unreal4.Errors;

/// <summary>
///     Represents a static of an Unreal player that was not found through its function or constructor.
/// </summary>
internal sealed record UnrealStaticError : ResultError
{
    private UnrealStaticError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for calls of a constructor that pass 2 different statics.
    /// </summary>
    /// <param name="type">The name of the type.</param>
    /// <param name="first">The RVA of the 1st static.</param>
    /// <param name="other">The RVA of the other static.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealStaticError ConstructedStaticAmbiguous(string type, uint first, uint other)
    {
        return new(UnrealSR.FormatConstructedStaticAmbiguous(type, $"{first:X}", $"{other:X}"));
    }

    /// <summary>
    ///     Creates the error for a constructor without a call that passes a static.
    /// </summary>
    /// <param name="type">The name of the type.</param>
    /// <param name="rva">The RVA of the constructor.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealStaticError ConstructedStaticNotFound(string type, uint rva)
    {
        return new(UnrealSR.FormatConstructedStaticNotFound(type, $"{rva:X}"));
    }

    /// <summary>
    ///     Creates the error for a function that doesn't read a static.
    /// </summary>
    /// <param name="function">The name of the function.</param>
    /// <param name="rva">The RVA of the function.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealStaticError FunctionStaticNotFound(string function, uint rva)
    {
        return new(UnrealSR.FormatFunctionStaticNotFound(function, $"{rva:X}"));
    }
}
