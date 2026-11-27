using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Locating;

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
    ///     Creates the root at the object in the slot of a global, like <c>GWorld</c>.
    /// </summary>
    /// <remarks>
    ///     Reads the pointer in the slot on each read, so an object that the game sets later shows up.
    /// </remarks>
    /// <param name="globalSlot">The slot of the global.</param>
    /// <returns>
    ///     The root.
    /// </returns>
    public static PathRoot Global(GlobalPointer globalSlot)
    {
        return new GlobalRoot(globalSlot);
    }

    /// <summary>
    ///     Creates the root at 1 live object.
    /// </summary>
    /// <remarks>
    ///     Reads the slot of the object in the object array and its class pointer on each read. Fails when the slot has another object
    ///     or none or the address has an object of another class, so a freed object doesn't give its last values
    ///     or the values of the next object at its address.
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
    ///     Creates the root at the 1st live object of the class called <paramref name="className"/>, like a subsystem.
    /// </summary>
    /// <remarks>
    ///     Keeps the object it finds and checks its class pointer on each read.
    ///     Searches the object array again on each read while the object is missing or gone,
    ///     which reads the array and only the objects in slots that changed.
    /// </remarks>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="className">The name of the class.</param>
    /// <returns>
    ///     The root.
    /// </returns>
    public static PathRoot FirstObject(ClassReader classReader, string className)
    {
        return new FirstObjectRoot(classReader, className);
    }
}

file sealed class GlobalRoot(
    GlobalPointer globalSlot) : PathRoot
{
    public override Result<Address> Read()
    {
        return globalSlot.Read();
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
        if (!classReader.IsLive(obj).TryUnwrap(out bool live, out var error))
        {
            return error;
        }

        return live ? obj.Address : _goneError ??= RootError.ObjectGone(obj.Class.Name, obj.Address);
    }
}

file sealed class FirstObjectRoot(
    ClassReader classReader,
    string className) : PathRoot
{
    // Keep the error, because a path reads its root on every tick.
    private RootError? _noLiveObjectError;
    private UnrealObject? _object;

    public override Result<Address> Read()
    {
        if (_object is { } kept)
        {
            if (classReader.ObjectReader.ClassOf(kept.Address).TryUnwrap(out var klass, out _) && klass == kept.Class.Address)
            {
                return kept.Address;
            }

            _object = null;
        }

        if (!classReader.Find(className).TryUnwrap(out var objectClass, out var error)
            || !classReader.InstancesOf(objectClass).TryUnwrap(out var objects, out error))
        {
            return error;
        }

        if (objects.Count == 0)
        {
            return _noLiveObjectError ??= RootError.NoLiveObject(className);
        }

        _object = objects[0];

        return _object.Address;
    }
}
