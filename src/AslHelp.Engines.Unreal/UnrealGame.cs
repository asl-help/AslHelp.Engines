using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a running game with the readers and globals of 1 profile.
/// </summary>
public sealed class UnrealGame
{
    private UnrealGame(
        INameTable names,
        ObjectReader objects,
        StructReader structs,
        IPropertyReader properties,
        IObjectArray objectArray,
        GlobalSlot gWorld)
    {
        Names = names;
        Objects = objects;
        Structs = structs;
        Properties = properties;
        ObjectArray = objectArray;
        GWorld = gWorld;
    }

    /// <summary>
    ///     Gets the slot of <c>GWorld</c>.
    /// </summary>
    public GlobalSlot GWorld { get; }

    /// <summary>
    ///     Gets the name table.
    /// </summary>
    internal INameTable Names { get; }

    /// <summary>
    ///     Gets the object array.
    /// </summary>
    internal IObjectArray ObjectArray { get; }

    /// <summary>
    ///     Gets the reader of objects.
    /// </summary>
    internal ObjectReader Objects { get; }

    /// <summary>
    ///     Gets the reader of properties.
    /// </summary>
    internal IPropertyReader Properties { get; }

    /// <summary>
    ///     Gets the reader of structs.
    /// </summary>
    internal StructReader Structs { get; }

    /// <summary>
    ///     Attaches to a game by finding the globals of <paramref name="profile"/> in <paramref name="module"/>.
    /// </summary>
    /// <remarks>
    ///     Searches the module once per global and doesn't read game data, so a game that's still loading attaches too.
    ///     A <c>GWorld</c> of 0 doesn't fail, because the game sets it once it loads a world.
    /// </remarks>
    /// <param name="memory">The memory of the game.</param>
    /// <param name="module">The module that the routes of the globals start at, usually the executable.</param>
    /// <param name="profile">The profile of the build of the game.</param>
    /// <returns>
    ///     The game or a failure when a global isn't found or a layout of <paramref name="profile"/> doesn't fit the pointer size of
    ///     <paramref name="memory"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/>, <paramref name="module"/> or <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public static Result<UnrealGame> Attach(IMemoryReader memory, MemoryModule module, UnrealProfile profile)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.NameTableLocator.Locate(memory, module).TryUnwrap(out var nameTableGlobal, out var error))
        {
            return AttachError.GlobalNotLocated(profile.NameTableShape.Global, module.Name, error);
        }

        if (!profile.ObjectArrayLocator.Locate(memory, module).TryUnwrap(out var objectArrayGlobal, out error))
        {
            return AttachError.GlobalNotLocated(UnrealProfile.ObjectArrayGlobal, module.Name, error);
        }

        // Locate the slot now, so a profile that doesn't fit the game fails here and not on the 1st read.
        GlobalSlot gWorld = new(memory, module, profile.WorldLocator, TimeSpan.Zero);

        if (gWorld.Locate().TryUnwrapErr(out error))
        {
            return AttachError.GlobalNotLocated(UnrealProfile.WorldGlobal, module.Name, error);
        }

        var names = profile.NameTableShape.Create(memory, nameTableGlobal);
        ObjectReader objects = new(memory, names, profile.ObjectLayout);
        StructReader structs = new(memory, objects, profile.StructLayout, profile.ChildLayout);

        if (!profile.ObjectArrayShape.Create(memory, objectArrayGlobal + profile.ObjectArrayOffset).TryUnwrap(out var objectArray, out error))
        {
            return error;
        }

        var properties = profile.PropertyShape.Create(memory, names, objects, structs);

        return new UnrealGame(names, objects, structs, properties, objectArray, gWorld);
    }
}
