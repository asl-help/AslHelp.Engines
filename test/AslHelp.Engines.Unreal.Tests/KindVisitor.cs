using AslHelp.Engines.Unreal.Properties;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Provides a visitor that returns the visit method it reached and the property it got, with the value type for <c>VisitValue</c>.
/// </summary>
internal sealed class KindVisitor : PropertyVisitor<(string Kind, UnrealProperty Property)>
{
    /// <inheritdoc/>
    public override (string Kind, UnrealProperty Property) VisitEnum(UnrealProperty prop)
    {
        return (nameof(VisitEnum), prop);
    }

    /// <inheritdoc/>
    public override (string Kind, UnrealProperty Property) VisitStruct(UnrealProperty prop)
    {
        return (nameof(VisitStruct), prop);
    }

    /// <inheritdoc/>
    public override (string Kind, UnrealProperty Property) VisitUnsupported(UnrealProperty prop)
    {
        return (nameof(VisitUnsupported), prop);
    }

    /// <inheritdoc/>
    public override (string Kind, UnrealProperty Property) VisitValue<T>(UnrealProperty prop)
    {
        return ($"{nameof(VisitValue)}<{typeof(T).Name}>", prop);
    }
}
