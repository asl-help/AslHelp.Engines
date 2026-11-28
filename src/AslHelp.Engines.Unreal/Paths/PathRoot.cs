using AslHelp.Engines.Profiles;
using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using System;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents the object that a <see cref="MemberPath"/> starts at, found again on each read of the path.
/// </summary>
internal abstract class PathRoot
{
    /// <summary>
    ///     Reads the address of the object.
    /// </summary>
    /// <returns>
    ///     The address, <see cref="Address.Zero"/> for a global whose slot is 0 or a failure when the object isn't there.
    /// </returns>
    public abstract Result<Address> Read();

    /// <summary>
    ///     Reads the class that the object of the root has or is declared as, which a step checks while the object isn't there.
    /// </summary>
    /// <returns>
    ///     The class or a failure when the class isn't loaded or doesn't read.
    /// </returns>
    public abstract Result<UnrealClass> ReadDeclaredClass();

    /// <summary>
    ///     Creates the root at the object in the slot of a global, like <c>GWorld</c>.
    /// </summary>
    /// <remarks>
    ///     Reads the pointer in the slot on each read, so an object that the game sets later shows up.
    /// </remarks>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="globalSlot">The slot of the global.</param>
    /// <param name="className">The name of the class that the global is declared as, like <c>World</c>.</param>
    /// <param name="globalName">The name of the global, like <c>GWorld</c>, for the error when the slot isn't found.</param>
    /// <param name="moduleName">The name of the module that the slot is searched in.</param>
    /// <param name="selection">The pick that the profile came from, so a slot that isn't found fails with the target and the picked version.</param>
    /// <returns>
    ///     The root.
    /// </returns>
    public static PathRoot Global(
        ClassReader classReader,
        GlobalPointer globalSlot,
        string className,
        string globalName,
        string moduleName,
        ProfileSelection<UnrealProfile>? selection)
    {
        return new GlobalRoot(classReader, globalSlot, className, globalName, moduleName, selection);
    }

    /// <summary>
    ///     Creates the root at 1 live object.
    /// </summary>
    /// <remarks>
    ///     Reads the class pointer of the object on each read and fails when the address has an object of another class,
    ///     so a freed object doesn't give the values of the next object at its address.
    /// </remarks>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="obj">The object.</param>
    /// <returns>
    ///     The root.
    /// </returns>
    public static PathRoot Object(ClassReader classReader, UnrealObject obj)
    {
        return new ObjectRoot(classReader, obj);
    }

    /// <summary>
    ///     Creates the root at the subsystem of the class called <paramref name="className"/> in the subsystem map of the game instance.
    /// </summary>
    /// <remarks>
    ///     Reads the game instance from <c>GEngine.GameInstance</c> and from <c>GWorld.OwningGameInstance</c> when the engine doesn't have
    ///     one, because <c>GWorld</c> is a passing world or null while a level loads. Keeps the subsystem while its class stays the same.
    /// </remarks>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="gEngine">The root at <c>GEngine</c>.</param>
    /// <param name="gWorld">The root at <c>GWorld</c>.</param>
    /// <param name="subsystemMapOffset">The offset of the subsystem map in the game instance.</param>
    /// <param name="className">The name of the class of the subsystem.</param>
    /// <returns>
    ///     The root.
    /// </returns>
    public static PathRoot Subsystem(
        ClassReader classReader,
        UnrealRoot gEngine,
        UnrealRoot gWorld,
        int subsystemMapOffset,
        string className)
    {
        return new SubsystemRoot(classReader, gEngine, gWorld, subsystemMapOffset, className);
    }
}

file sealed class GlobalRoot(
    ClassReader classReader,
    GlobalPointer globalSlot,
    string className,
    string globalName,
    string moduleName,
    ProfileSelection<UnrealProfile>? selection) : PathRoot
{
    private ResultError? _cause;
    private ResultError? _error;

    public override Result<Address> Read()
    {
        if (globalSlot.Locate().TryUnwrapErr(out var cause))
        {
            // Keep the error until the next search, because the slot returns the same cause until then.
            if (!ReferenceEquals(cause, _cause))
            {
                ResultError notLocated = AttachError.GlobalNotLocated(globalName, moduleName, cause);
                _error = selection is null ? notLocated : AttachError.ProfileMisfit(selection.Target, selection.Version, notLocated);
                _cause = cause;
            }

            return _error!;
        }

        return globalSlot.Read();
    }

    public override Result<UnrealClass> ReadDeclaredClass()
    {
        return classReader.Find(className);
    }
}

file sealed class ObjectRoot(
    ClassReader classReader,
    UnrealObject obj) : PathRoot
{
    // Keep the error, because a path reads its root on every tick.
    private RootError? _goneError;

    public override Result<Address> Read()
    {
        if (!classReader.ObjectReader.ClassOf(obj.Address).TryUnwrap(out var klass, out var error))
        {
            return error;
        }

        return klass == obj.Class.Address ? obj.Address : _goneError ??= RootError.ObjectGone(obj.Class.Name, obj.Address);
    }

    public override Result<UnrealClass> ReadDeclaredClass()
    {
        return obj.Class;
    }
}

file sealed class SubsystemRoot(
    ClassReader classReader,
    UnrealRoot gEngine,
    UnrealRoot gWorld,
    int subsystemMapOffset,
    string className) : PathRoot
{
    private const string SubsystemMapName = "SubsystemMap";

    private Result<IMemberPath>? _engineInstance;
    private Result<IMemberPath>? _worldInstance;
    private SparseArray? _subsystemMap;
    private byte[] _subsystemPairs = [];
    private RootError? _notFoundError;
    // Keep the subsystem with its class, so a later read only checks the class.
    private (Address Address, Address Class)? _subsystem;

    public override Result<Address> Read()
    {
        if (_subsystem is { } kept)
        {
            if (classReader.ObjectReader.ClassOf(kept.Address).TryUnwrap(out var klass, out _) && klass == kept.Class)
            {
                return kept.Address;
            }

            _subsystem = null;
        }

        if (!classReader.Find(className).TryUnwrap(out var subsystemClass, out var error))
        {
            return error;
        }

        if (!ReadGameInstance().TryUnwrap(out var gameInstance, out error))
        {
            return error;
        }

        if (gameInstance == Address.Zero)
        {
            return _notFoundError ??= RootError.SubsystemNotFound(className);
        }

        if (!FindSubsystem(gameInstance, subsystemClass.Address).TryUnwrap(out var subsystem, out error))
        {
            return error;
        }

        if (subsystem == Address.Zero)
        {
            return _notFoundError ??= RootError.SubsystemNotFound(className);
        }

        _subsystem = (subsystem, subsystemClass.Address);

        return subsystem;
    }

    public override Result<UnrealClass> ReadDeclaredClass()
    {
        return classReader.Find(className);
    }

    // Read the game instance of the engine 1st, because it outlives every world. Return 0 when neither has one and the failure of the
    // engine when neither reads. Build a step again after a failed check, because a step checks the classes the game has at that moment.
    private Result<Address> ReadGameInstance()
    {
        if (_engineInstance is not { IsOk: true })
        {
            _engineInstance = gEngine.Path.Member("GameInstance");
        }

        var fromEngine = _engineInstance.Value.Read<Address>();

        if (fromEngine.TryUnwrap(out var gameInstance, out var engineError) && gameInstance != Address.Zero)
        {
            return gameInstance;
        }

        if (_worldInstance is not { IsOk: true })
        {
            _worldInstance = gWorld.Path.Member("OwningGameInstance");
        }

        var fromWorld = _worldInstance.Value.Read<Address>();

        if (fromWorld.TryUnwrap(out gameInstance, out var worldError))
        {
            return gameInstance;
        }

        return engineError is null ? worldError : engineError;
    }

    // Find the pair whose key is the class, because the map keys each subsystem by its class.
    // A pair is the class pointer, the subsystem pointer and the 2 ints of its hash chain.
    private Result<Address> FindSubsystem(Address gameInstance, Address subsystemClass)
    {
        var memory = classReader.Memory;

        if (_subsystemMap is null)
        {
            if (!classReader.KindLayouts.Find((MapKind)PropertyKinds.Find("MapProperty")).TryUnwrap(out var layout, out var error))
            {
                return error;
            }

            _subsystemMap = new SparseArray(
                SubsystemMapName,
                layout.Set.SparseArray,
                subsystemMapOffset + layout.Pairs + layout.Set.Elements,
                classReader.Bounds.MaxElements);
        }

        if (!_subsystemMap.ReadSlots(memory, gameInstance).TryUnwrap(out var slots, out var slotsError))
        {
            return slotsError;
        }

        int pointerSize = memory.Layout.PointerSize;
        int stride = (2 * pointerSize) + (2 * sizeof(int));
        int length = slots.SlotCount * stride;

        if (_subsystemPairs.Length < length)
        {
            _subsystemPairs = new byte[length];
        }

        var pairs = _subsystemPairs.AsSpan(0, length);

        // Read every pair with 1 read, because a tick before the game makes the subsystem would read each used slot otherwise.
        if (length > 0 && memory.Read(slots.Data, pairs).TryUnwrapErr(out var pairsError))
        {
            return ValueError.Unreadable(gameInstance, SubsystemMapName, slots.Data, pairsError);
        }

        for (int slot = 0; slot < slots.SlotCount; slot++)
        {
            if (!_subsystemMap.IsUsed(slot))
            {
                continue;
            }

            var pair = pairs.Slice(slot * stride, stride);

            if (ObjectSlots.PointerAt(pair.Slice(0, pointerSize), memory.Layout.ByteOrder) == subsystemClass)
            {
                return ObjectSlots.PointerAt(pair.Slice(pointerSize, pointerSize), memory.Layout.ByteOrder);
            }
        }

        return Address.Zero;
    }
}
