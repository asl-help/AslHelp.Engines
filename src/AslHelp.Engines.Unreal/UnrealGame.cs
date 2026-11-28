using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Paths;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a running game with the readers and globals of 1 profile.
/// </summary>
/// <remarks>
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealGame
{
    private readonly ClassReader _classReader;
    private readonly bool _hasSubsystems;

    private UnrealGame(
        IMemoryReader memory,
        ObjectReader objectReader,
        IPropertyReader propertyReader,
        IObjectArray objectArray,
        ClassReader classReader,
        GlobalPointer gWorldSlot,
        GlobalPointer gEngineSlot,
        bool hasSubsystems)
    {
        Memory = memory;
        ObjectReader = objectReader;
        PropertyReader = propertyReader;
        ObjectArray = objectArray;
        _classReader = classReader;
        GWorld = new UnrealRoot(classReader, PathRoot.Global(gWorldSlot), UnrealProfile.WorldGlobal);
        GEngine = new UnrealRoot(classReader, PathRoot.Global(gEngineSlot), UnrealProfile.EngineGlobal);
        _hasSubsystems = hasSubsystems;
    }

    /// <summary>
    ///     Gets the root at the <c>GEngine</c> global.
    /// </summary>
    /// <remarks>
    ///     Each read of a path from the root reads the pointer in the slot again, so an engine that the game creates later shows up.
    /// </remarks>
    public UnrealRoot GEngine { get; }

    /// <summary>
    ///     Gets the root at the <c>GWorld</c> global, like <c>GWorld.Path.Member("AuthorityGameMode")</c>.
    /// </summary>
    /// <remarks>
    ///     Each read of a path from the root reads the pointer in the slot again, so a world that the game loads later shows up.
    ///     On Unreal Engine 3 the class <c>World</c> doesn't have any reflected properties, so start at <see cref="GEngine"/>
    ///     or at an object of <see cref="UnrealClass.GetInstances"/>.
    /// </remarks>
    public UnrealRoot GWorld { get; }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    internal IMemoryReader Memory { get; }

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
    ///     Attaches to a game by finding the globals of <paramref name="profile"/> in <paramref name="module"/>.
    /// </summary>
    /// <remarks>
    ///     Searches the module once per global and doesn't read game data, so a game that's still loading attaches too.
    ///     A <c>GWorld</c> or <c>GEngine</c> of 0 doesn't fail, because the game sets it later.
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
            return AttachError.GlobalNotLocated(profile.ObjectArrayGlobal, module.Name, error);
        }

        // Locate the slot now, so a profile that doesn't fit the game fails here and not on the 1st read.
        GlobalPointer gWorldSlot = new(memory, module, profile.WorldLocator, TimeSpan.Zero);

        if (gWorldSlot.Locate().TryUnwrapErr(out error))
        {
            return AttachError.GlobalNotLocated(UnrealProfile.WorldGlobal, module.Name, error);
        }

        GlobalPointer gEngineSlot = new(memory, module, profile.EngineLocator, TimeSpan.Zero);

        if (gEngineSlot.Locate().TryUnwrapErr(out error))
        {
            return AttachError.GlobalNotLocated(UnrealProfile.EngineGlobal, module.Name, error);
        }

        var nameTable = profile.NameTableShape.Create(memory, nameTableGlobal);
        ObjectReader objectReader = new(memory, nameTable, profile.ObjectLayout);
        StructReader structReader = new(memory, objectReader, profile.StructLayout, profile.ChildLayout);

        if (!profile.ObjectArrayShape.Create(memory, objectArrayGlobal + profile.ObjectArrayOffset).TryUnwrap(out var objectArray, out error))
        {
            return error;
        }

        var propertyReader = profile.PropertyShape.Create(memory, nameTable, objectReader, structReader);

        ClassReader classReader = new(memory, nameTable, objectReader, structReader, propertyReader, objectArray, profile.KindLayouts);

        return new UnrealGame(memory, objectReader, propertyReader, objectArray, classReader, gWorldSlot, gEngineSlot, profile.HasSubsystems);
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
    ///     The 1st call reads every object of the array and keeps the name of every class and every package.
    ///     A later call checks the name and the class of a kept class. A miss reads the array again,
    ///     the objects in array slots that changed and the name of each kept Blueprint class.
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
    ///     Finds the loaded package called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Ignores case, like the engine. Searches the object array and keeps what it finds like <see cref="FindClass"/>.
    ///     Finds only packages without an outer object, so the groups inside an Unreal Engine 3 package don't match.
    /// </remarks>
    /// <param name="name">The name of the package, like <c>/Script/Engine</c> or <c>Engine</c> on Unreal Engine 3.</param>
    /// <returns>
    ///     The package or a failure when no loaded package has the name or a read of the search fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealPackage> FindPackage(string name)
    {
        return _classReader.FindPackage(name);
    }

    /// <summary>
    ///     Creates a root at the 1st live object of the class called <paramref name="className"/>, like a game instance subsystem.
    /// </summary>
    /// <remarks>
    ///     Doesn't read memory and doesn't check that the class is a subsystem. A read of a path from the root finds the object and keeps it,
    ///     so a subsystem that the game creates later shows up and a read finds a replaced subsystem again.
    /// </remarks>
    /// <param name="className">The name of the class without a prefix, like <c>MySubsystem</c>.</param>
    /// <returns>
    ///     The root or a failure when the profile doesn't have <c>UGameInstance.SubsystemCollection</c>, like a profile before 4.22.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="className"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealRoot> FindSubsystem(string className)
    {
        ArgumentNullException.ThrowIfNull(className);

        if (!_hasSubsystems)
        {
            return RootError.SubsystemsUnavailable();
        }

        return new UnrealRoot(_classReader, PathRoot.FirstObject(_classReader, className), className);
    }

    /// <summary>
    ///     Lists the loaded packages.
    /// </summary>
    /// <remarks>
    ///     Searches the object array like <see cref="FindPackage"/>.
    /// </remarks>
    /// <returns>
    ///     The packages in array order or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealPackage>> GetPackages()
    {
        return _classReader.GetPackages();
    }

    /// <summary>
    ///     Reads the value of <paramref name="property"/> in <paramref name="obj"/> as <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    ///     The 1st read of a property reads what its kind needs and keeps it, like the mask of a bool.
    /// </remarks>
    /// <typeparam name="T">
    ///     The type that the kind of <paramref name="property"/> reads: <see cref="sbyte"/>, <see cref="short"/>, <see cref="int"/>,
    ///     <see cref="long"/>, <see cref="ushort"/>, <see cref="uint"/>, <see cref="ulong"/>, <see cref="float"/> and <see cref="double"/>
    ///     for the number properties, <see cref="byte"/> for <c>ByteProperty</c>, <see cref="bool"/> for <c>BoolProperty</c>,
    ///     the type of the underlying property for <c>EnumProperty</c>, <see cref="string"/> for <c>NameProperty</c> and <c>StrProperty</c>,
    ///     <see cref="Address"/> for the object properties and <c>WeakObjectProperty</c>, an array of the element type for <c>ArrayProperty</c>,
    ///     a <see cref="HashSet{T}"/> for <c>SetProperty</c>,
    ///     a <see cref="Dictionary{TKey, TValue}"/> for <c>MapProperty</c>,
    ///     <see cref="StructValue"/> for <c>StructProperty</c>
    ///     and also an array of the number type for a <c>StructProperty</c> whose members are numbers of 1 class.
    /// </typeparam>
    /// <param name="obj">The address of the object.</param>
    /// <param name="property">The property.</param>
    /// <returns>
    ///     The value or a failure when the kind reads another type or doesn't have a value, <paramref name="obj"/> is <see cref="Address.Zero"/>,
    ///     the profile doesn't have the offsets of the kind or a read fails. An object pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="property"/> is <see langword="null"/>.
    /// </exception>
    public Result<T> Read<T>(Address obj, UnrealProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.Read<T>(Memory, obj);
    }
}
