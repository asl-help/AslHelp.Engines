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
    private readonly ClassReader _classReader;

    private UnrealGame(
        IMemoryReader memory,
        INameTable nameTable,
        ObjectReader objectReader,
        StructReader structReader,
        IPropertyReader propertyReader,
        IObjectArray objectArray,
        ClassReader classReader,
        GlobalSlot gWorldSlot)
    {
        Memory = memory;
        NameTable = nameTable;
        ObjectReader = objectReader;
        StructReader = structReader;
        PropertyReader = propertyReader;
        ObjectArray = objectArray;
        _classReader = classReader;

        // Create the hop of the global once, because the path and the chain start at the same global.
        var gWorldHop = MemberHop.Global(UnrealProfile.WorldGlobal);
        GWorld = new UnrealObjectPath(new MemberPath(this, gWorldSlot, [gWorldHop]));
        GWorldChain = new MemberChain<MemberHop>(new MemberResolver(this, gWorldSlot), gWorldHop);
    }

    /// <summary>
    ///     Gets the path that starts at the <c>GWorld</c> global.
    /// </summary>
    /// <remarks>
    ///     Getting the path doesn't read memory.
    ///     Each read of a longer path reads the pointer in the slot again, so a world that the game loads later shows up.
    /// </remarks>
    public UnrealObjectPath GWorld { get; }

    /// <summary>
    ///     Gets the member chain that starts at the <c>GWorld</c> global, for a script that writes members through <see langword="dynamic"/>.
    /// </summary>
    /// <remarks>
    ///     Every chain is an <see cref="IWatcherSource"/> that reads its end as an <c>IntProperty</c>, like
    ///     <c>GWorldChain.AuthorityGameMode.FixtureData.Hp</c>.
    ///     A misspelled member or another kind fails when the watcher reads and not on the script line,
    ///     because the class of an object is only there at read time.
    /// </remarks>
    public dynamic GWorldChain { get; }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    internal IMemoryReader Memory { get; }

    /// <summary>
    ///     Gets the name table.
    /// </summary>
    internal INameTable NameTable { get; }

    /// <summary>
    ///     Gets the object array.
    /// </summary>
    internal IObjectArray ObjectArray { get; }

    /// <summary>
    ///     Gets the reader of objects.
    /// </summary>
    internal ObjectReader ObjectReader { get; }

    /// <summary>
    ///     Gets the reader of properties.
    /// </summary>
    internal IPropertyReader PropertyReader { get; }

    /// <summary>
    ///     Gets the reader of structs.
    /// </summary>
    internal StructReader StructReader { get; }

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
        GlobalSlot gWorldSlot = new(memory, module, profile.WorldLocator, TimeSpan.Zero);

        if (gWorldSlot.Locate().TryUnwrapErr(out error))
        {
            return AttachError.GlobalNotLocated(UnrealProfile.WorldGlobal, module.Name, error);
        }

        var nameTable = profile.NameTableShape.Create(memory, nameTableGlobal);
        ObjectReader objectReader = new(memory, nameTable, profile.ObjectLayout);
        StructReader structReader = new(memory, objectReader, profile.StructLayout, profile.ChildLayout);

        if (!profile.ObjectArrayShape.Create(memory, objectArrayGlobal + profile.ObjectArrayOffset).TryUnwrap(out var objectArray, out error))
        {
            return error;
        }

        var propertyReader = profile.PropertyShape.Create(memory, nameTable, objectReader, structReader);

        ClassReader classReader = new(objectReader, structReader, propertyReader, objectArray);

        return new UnrealGame(memory, nameTable, objectReader, structReader, propertyReader, objectArray, classReader, gWorldSlot);
    }

    /// <summary>
    ///     Reads the real class of <paramref name="obj"/>.
    /// </summary>
    /// <remarks>
    ///     The class can derive from the class that the property pointing at the object declares.
    ///     Keeps each class it finds for the game, so the next call for an object of that class reads only the class pointer and the class name.
    ///     Returns a new class when the name changed, because the game can free a Blueprint class and reuse its address.
    /// </remarks>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The class or a failure when <paramref name="obj"/> is <see cref="Address.Zero"/>, its class pointer is 0 or a read fails.
    /// </returns>
    public Result<UnrealClass> ClassOf(Address obj)
    {
        return _classReader.ClassOf(obj);
    }

    /// <summary>
    ///     Finds the loaded class called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally. Returns an object of the object array whose class is <c>Class</c> or derives from it,
    ///     as <c>BlueprintGeneratedClass</c> does. When 2 classes have the name, returns the one a call found 1st, in array order within 1 call.
    ///     The 1st call reads every object of the array and keeps the name of every class.
    ///     A later call checks the name and the class of a kept class. A miss only reads the objects in array slots that changed
    ///     and the name of each kept Blueprint class.
    ///     Skips an object that doesn't read and fails with that read only when nothing matches.
    ///     A miss doesn't get kept, so a class that loads later shows up on the next call.
    ///     A new class that takes both the slot and the address of a freed object that wasn't a Blueprint class isn't found
    ///     until a call reads every slot again.
    /// </remarks>
    /// <param name="name">The name of the class without a prefix, like <c>Actor</c> or <c>BP_Player_C</c>.</param>
    /// <returns>
    ///     The class or a failure when no loaded class has the name or a read of the search fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealClass> FindClass(string name)
    {
        return _classReader.Find(name);
    }

    /// <summary>
    ///     Reads the value of <paramref name="property"/> in <paramref name="obj"/> as a 4-byte signed integer.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="property">The property, which has to be an <c>IntProperty</c>.</param>
    /// <returns>
    ///     The value or a failure when <paramref name="property"/> is another kind, <paramref name="obj"/> is <see cref="Address.Zero"/>
    ///     or the read fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="property"/> is <see langword="null"/>.
    /// </exception>
    public Result<int> ReadInt32(Address obj, UnrealProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.ReadInt32(Memory, obj);
    }

    /// <summary>
    ///     Reads the value of <paramref name="property"/> in <paramref name="obj"/> as an object pointer.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="property">The property, which has to be an object property like <c>ObjectProperty</c> or <c>ClassProperty</c>.</param>
    /// <returns>
    ///     The address of the object it points at or a failure when <paramref name="property"/> isn't an object property,
    ///     <paramref name="obj"/> is <see cref="Address.Zero"/> or the read fails. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="property"/> is <see langword="null"/>.
    /// </exception>
    public Result<Address> ReadObject(Address obj, UnrealProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.ReadObject(Memory, obj);
    }
}
