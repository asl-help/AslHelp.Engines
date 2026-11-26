namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Provides a visitor that returns the name of the visit method it reached and the property it got.
/// </summary>
internal sealed class KindVisitor : IPropertyVisitor<(string Kind, UnrealProperty Property)>
{
    /// <inheritdoc/>
    public (string Kind, UnrealProperty Property) VisitInt32(UnrealProperty prop)
    {
        return (nameof(VisitInt32), prop);
    }

    /// <inheritdoc/>
    public (string Kind, UnrealProperty Property) VisitObject(UnrealProperty prop)
    {
        return (nameof(VisitObject), prop);
    }

    /// <inheritdoc/>
    public (string Kind, UnrealProperty Property) VisitUnsupported(UnrealProperty prop)
    {
        return (nameof(VisitUnsupported), prop);
    }
}
