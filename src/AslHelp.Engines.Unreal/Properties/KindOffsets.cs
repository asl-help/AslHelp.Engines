using AslHelp.Engines.Unreal.Errors;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Reads the offsets of 1 kind from a <see cref="KindProfile"/> and collects the path of each missing offset.
/// </summary>
/// <param name="profile">The profile to read from.</param>
/// <param name="className">The property class of the kind, like <c>BoolProperty</c>, for the failure.</param>
internal sealed class KindOffsets(
    KindProfile profile,
    string className)
{
    private readonly string _className = className;
    private readonly KindProfile _profile = profile;

    private List<string>? _missing;

    /// <summary>
    ///     Creates the layout of the kind when every offset read so far is there.
    /// </summary>
    /// <typeparam name="TLayout">The type of the layout.</typeparam>
    /// <param name="layout">The layout, built from the offsets read so far.</param>
    /// <returns>
    ///     <paramref name="layout"/> or a failure with the class and the path of every missing offset.
    /// </returns>
    public Result<TLayout> Finish<TLayout>(TLayout layout)
    {
        return _missing is null ? layout : ProfileMapError.KindOffsetsMissing(_className, _missing);
    }

    /// <summary>
    ///     Checks whether the profile has the offset of <paramref name="member"/> in <paramref name="type"/>, without counting it as missing.
    /// </summary>
    /// <param name="type">The type, like <c>FScriptSparseSetLayout</c>.</param>
    /// <param name="member">The member, like <c>Size</c>.</param>
    /// <returns>
    ///     <see langword="true"/> when the profile has the offset.
    /// </returns>
    public bool Has(string type, string member)
    {
        return _profile.Read(type, member) is not null;
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type, like <c>FScriptArray</c>.</param>
    /// <param name="member">The member, like <c>Data</c>.</param>
    /// <returns>
    ///     The offset or 0 when the profile doesn't have it. <see cref="Finish"/> then fails with its path.
    /// </returns>
    public int Read(string type, string member)
    {
        if (_profile.Read(type, member) is { } offset)
        {
            return offset;
        }

        (_missing ??= []).Add($"offsets.{type}.{member}");

        return 0;
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in the property class <paramref name="propertyClass"/> with the prefix of the profile.
    /// </summary>
    /// <param name="propertyClass">The property class without a prefix, like <c>ArrayProperty</c>.</param>
    /// <param name="member">The member, like <c>Inner</c>.</param>
    /// <returns>
    ///     The offset or 0 when the profile doesn't have it. <see cref="Finish"/> then fails with its path.
    /// </returns>
    public int ReadProperty(string propertyClass, string member)
    {
        return Read(_profile.PropertyPrefix + propertyClass, member);
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/>, which only some engine versions have.
    /// </summary>
    /// <param name="type">The type, like <c>FString</c>.</param>
    /// <param name="member">The member, like <c>Data</c>.</param>
    /// <returns>
    ///     The offset or <see langword="null"/> when the profile doesn't have it.
    /// </returns>
    public int? ReadOrNull(string type, string member)
    {
        return _profile.Read(type, member);
    }
}
