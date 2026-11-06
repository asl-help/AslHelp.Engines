using AslHelp.Memory;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Represents a failed measurement of a UDK player or a list of targets that is not valid.
/// </summary>
internal sealed record UdkError : ResultError
{
    private UdkError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a class that does not exist.
    /// </summary>
    /// <param name="name">The name of the class.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ClassNotFound(string name)
    {
        return new(UdkSR.FormatClassNotFound(name));
    }

    /// <summary>
    ///     Creates the error for a class offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ClassOffsetNotFound()
    {
        return new(UdkSR.ClassOffsetNotFound);
    }

    /// <summary>
    ///     Creates the error for a file that is not a PE file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ExecutableNotPe(string path)
    {
        return new(SR.FormatExecutableNotPe(path));
    }

    /// <summary>
    ///     Creates the error for a file that did not open or read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FileUnreadable(string path, string reason)
    {
        return new(UdkSR.FormatFileUnreadable(path, reason));
    }

    /// <summary>
    ///     Creates the error for a member that the fixture check reads through and that was not found.
    /// </summary>
    /// <param name="member">The report key of the member.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureCheckNeeds(string member)
    {
        return new(UdkSR.FormatFixtureCheckNeeds(member));
    }

    /// <summary>
    ///     Creates the error for a chain of fields of the fixture class that did not read.
    /// </summary>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureChildrenUnreadable(ResultError cause)
    {
        return new($"{UdkSR.FixtureChildrenUnreadable} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture class without the field and with a field whose name did not read.
    /// </summary>
    /// <param name="cause">The error of the 1st name that did not read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureFieldNameUnreadable(ResultError cause)
    {
        return new($"{UdkSR.FixtureFieldNameUnreadable} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture class without the field.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureFieldNotFound()
    {
        return new(UdkSR.FixtureFieldNotFound);
    }

    /// <summary>
    ///     Creates the error for a fixture class without a spawned object.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureInstanceNotFound()
    {
        return new(UdkSR.FixtureInstanceNotFound);
    }

    /// <summary>
    ///     Creates the error for an offset of the fixture field that did not read.
    /// </summary>
    /// <param name="address">The address of the offset.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureOffsetUnreadable(Address address, ResultError cause)
    {
        return new($"{UdkSR.FormatFixtureOffsetUnreadable(address)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture value that did not read.
    /// </summary>
    /// <param name="address">The address of the value.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureValueUnreadable(Address address, ResultError cause)
    {
        return new($"{UdkSR.FormatFixtureValueUnreadable(address)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture value that is not the value the fixture game sets.
    /// </summary>
    /// <param name="expected">The value the fixture game sets.</param>
    /// <param name="actual">The value read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError FixtureValueWrong(int expected, int actual)
    {
        return new(UdkSR.FormatFixtureValueWrong(expected, actual));
    }

    /// <summary>
    ///     Creates the error for an executable that the server did not load.
    /// </summary>
    /// <param name="name">The file name of the executable.</param>
    /// <param name="cause">The error of the module lookup.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ModuleNotLoaded(string name, ResultError cause)
    {
        return new(UdkSR.FormatModuleNotLoaded(name, cause.Message));
    }

    /// <summary>
    ///     Creates the error for 2 name arrays with different data that both fit.
    /// </summary>
    /// <param name="firstRva">The RVA of the 1st array in hex.</param>
    /// <param name="otherRva">The RVA of the other array in hex.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError NameArrayAmbiguous(string firstRva, string otherRva)
    {
        return new(UdkSR.FormatNameArrayAmbiguous(firstRva, otherRva));
    }

    /// <summary>
    ///     Creates the error for a first name of the name array that did not read.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="id">The position of the name.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError NameArrayNameUnreadable(string rva, int id, ResultError cause)
    {
        return new($"{UdkSR.FormatNameArrayNameUnreadable(rva, id)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a name array that was not found.
    /// </summary>
    /// <param name="first">The 1st name the search starts from.</param>
    /// <param name="second">The 2nd name the search starts from.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError NameArrayNotFound(string first, string second)
    {
        return new(UdkSR.FormatNameArrayNotFound(first, second));
    }

    /// <summary>
    ///     Creates the error for a name array with another name at a position of the first names.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="actual">The name read.</param>
    /// <param name="id">The position of the name.</param>
    /// <param name="expected">The first name at that position.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError NameArrayWrongName(string rva, string actual, int id, string expected)
    {
        return new(UdkSR.FormatNameArrayWrongName(rva, actual, id, expected));
    }

    /// <summary>
    ///     Creates the error for a name offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError NameOffsetNotFound()
    {
        return new(UdkSR.NameOffsetNotFound);
    }

    /// <summary>
    ///     Creates the error for 2 object arrays with different data and the same count that both fit.
    /// </summary>
    /// <param name="firstRva">The RVA of the 1st array in hex.</param>
    /// <param name="otherRva">The RVA of the other array in hex.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ObjectArrayAmbiguous(string firstRva, string otherRva)
    {
        return new(UdkSR.FormatObjectArrayAmbiguous(firstRva, otherRva));
    }

    /// <summary>
    ///     Creates the error for an object array that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ObjectArrayNotFound()
    {
        return new(UdkSR.ObjectArrayNotFound);
    }

    /// <summary>
    ///     Creates the error for an object array that did not read to its end after the search found it.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError ObjectArrayUnreadable(string rva, ResultError cause)
    {
        return new($"{UdkSR.FormatObjectArrayUnreadable(rva)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for an outer offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError OuterOffsetNotFound()
    {
        return new(UdkSR.OuterOffsetNotFound);
    }

    /// <summary>
    ///     Creates the error for a server that exited during the measurement.
    /// </summary>
    /// <param name="exitCode">The exit code of the server.</param>
    /// <param name="cause">The error of the measurement or <see langword="null"/> if the measurement passed.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError PlayerExitedDuringMeasurement(int exitCode, ResultError? cause)
    {
        string message = UdkSR.FormatPlayerExitedDuringMeasurement(exitCode);

        return new(cause is null ? message : $"{message} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a target with a blank value.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsBlankValue(string path, string global)
    {
        return new(UdkSR.FormatTargetsBlankValue(path, global));
    }

    /// <summary>
    ///     Creates the error for a file without targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsEmpty(string path)
    {
        return new(UdkSR.FormatTargetsEmpty(path));
    }

    /// <summary>
    ///     Creates the error for a name array with too few first names.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <param name="least">The smallest number of first names.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsFewFirstNames(string path, string global, int least)
    {
        return new(UdkSR.FormatTargetsFewFirstNames(path, global, least));
    }

    /// <summary>
    ///     Creates the error for a file that does not parse as targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the parser.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsInvalid(string path, string reason)
    {
        return new(UdkSR.FormatTargetsInvalid(path, reason));
    }

    /// <summary>
    ///     Creates the error for a file without exactly 1 name array.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsNeedsOneNameArray(string path)
    {
        return new(UdkSR.FormatTargetsNeedsOneNameArray(path));
    }

    /// <summary>
    ///     Creates the error for a file without exactly 1 object array.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsNeedsOneObjectArray(string path)
    {
        return new(UdkSR.FormatTargetsNeedsOneObjectArray(path));
    }

    /// <summary>
    ///     Creates the error for an assembly without the embedded targets.
    /// </summary>
    /// <param name="resource">The name of the resource.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsNotEmbedded(string resource)
    {
        return new(UdkSR.FormatTargetsNotEmbedded(resource));
    }

    /// <summary>
    ///     Creates the error for a targets file that does not exist.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsNotFound(string path)
    {
        return new(UdkSR.FormatTargetsNotFound(path));
    }

    /// <summary>
    ///     Creates the error for a null target.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkError TargetsNull(string path, string global)
    {
        return new(UdkSR.FormatTargetsNull(path, global));
    }
}
