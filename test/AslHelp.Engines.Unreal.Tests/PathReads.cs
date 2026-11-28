using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Provides 1 read of the end of a path without a watcher, so a test checks the value or the failure of 1 read.
/// </summary>
internal static class PathReads
{
    extension(Result<IMemberPath> path)
    {
        /// <summary>
        ///     Reads the address of the object or struct at the end of the path.
        /// </summary>
        /// <returns>
        ///     The address, the failure of the step that created the path or the failure of the read.
        /// </returns>
        public Result<Address> Read()
        {
            return path.TryUnwrap(out var end, out var error) ? ((UnrealPath)end).Read() : error;
        }
    }

    extension<T>(Result<IWatcherSource<T>> source)
    {
        /// <summary>
        ///     Reads the value at the end of the path.
        /// </summary>
        /// <returns>
        ///     The value, the failure of the step that created the source or the failure of the read.
        /// </returns>
        public Result<T> Read()
        {
            return source.TryUnwrap(out var end, out var error) ? ((UnrealValuePath<T>)end).Read() : error;
        }
    }
}
