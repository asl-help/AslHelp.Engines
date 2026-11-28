using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of a property whose value is the address of an object, which a path goes on through.
/// </summary>
/// <param name="property">The object or weak object property.</param>
/// <param name="classReader">The reader of the game.</param>
/// <param name="propertyClass">
///     The offset of the pointer to the declared class in the property or <see langword="null"/> when the profile doesn't have it.
/// </param>
internal abstract class PointerAccess(
    UnrealProperty property,
    ClassReader classReader,
    int? propertyClass) : ValueAccess<Address>(property)
{
    private DeclaredContainer? _declared;

    /// <inheritdoc/>
    public sealed override bool StepReadsMemory => true;

    /// <summary>
    ///     Gets the reader of the game.
    /// </summary>
    protected ClassReader ClassReader { get; } = classReader;

    /// <inheritdoc/>
    public sealed override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return Read(memory, container, Address.Zero).TryUnwrap(out var target, out var error)
            ? new Container(target, null)
            : error;
    }

    // Keep the declared class once it reads, because the engine doesn't change a loaded property.
    /// <inheritdoc/>
    public sealed override Result<DeclaredContainer?> StepDeclared(IMemoryReader memory)
    {
        if (_declared is null && propertyClass is { } offset && ClassReader.PropertyClassOf(Property, offset) is { } klass)
        {
            _declared = new DeclaredContainer(klass, IsObject: true);
        }

        return Result.Ok(_declared);
    }
}
