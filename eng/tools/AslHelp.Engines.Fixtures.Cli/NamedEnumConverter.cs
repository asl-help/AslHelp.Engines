using System;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads and writes a <typeparamref name="TEnum"/> as the name of its member.
/// </summary>
/// <remarks>
///     Refuses a number, because the default converter reads <c>0</c> as the 1st member and any other number as a value without a
///     member.
/// </remarks>
/// <typeparam name="TEnum">The enum to read and write.</typeparam>
internal sealed class NamedEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
