using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Diagnostics;
using System.IO;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the file version of a <see cref="MemoryModule"/>, which <see cref="UnrealTarget"/> reads the engine version from.
/// </summary>
internal static class MemoryModuleExtensions
{
    extension(MemoryModule module)
    {
        /// <summary>
        ///     Gets the file version in the version resource of the module file or a failure when the path isn't known or the
        ///     file doesn't read.
        /// </summary>
        /// <remarks>
        ///     Reads the file on each use.
        /// </remarks>
        public Result<ModuleFileVersion> FileVersion
        {
            get
            {
                if (module.Path is not { } path)
                {
                    return TargetError.ModulePathUnknown(module.Name);
                }

                try
                {
                    // Keep the file open while GetVersionInfo reads it, because GetVersionInfo returns an empty version for a file
                    // it can't open.
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                    var info = FileVersionInfo.GetVersionInfo(path);
                    string? text = string.IsNullOrEmpty(info.FileVersion) ? null : info.FileVersion;

                    return new ModuleFileVersion(text, new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart));
                }
                // Catch ArgumentException and NotSupportedException too, because an empty or malformed path throws them.
                catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
                {
                    return TargetError.ModuleFileUnreadable(path, ex.Message);
                }
            }
        }
    }
}
