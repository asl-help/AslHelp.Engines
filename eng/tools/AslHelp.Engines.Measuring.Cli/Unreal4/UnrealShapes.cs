using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Lists the layouts that moved across engine versions, so the plugins and the library can pick a reader per version.
/// </summary>
/// <remarks>
///     <see cref="NameOf"/> spells each layout as <c>$shape</c> in the engine library profiles.
/// </remarks>
internal static class UnrealShapes
{
    private static readonly Version _first = new(4, 0);

    private static readonly (string Type, (Version From, UnrealShape Shape)[] Shapes)[] _table =
    [
        ("FField", [(_first, UnrealShape.UProperty), (new(4, 25), UnrealShape.FField)]),
        ("FNameEntry", [(_first, UnrealShape.Indirect), (new(4, 23), UnrealShape.Chunked)]),
        (
            "FUObjectArray",
            [(_first, UnrealShape.Pointers), (new(4, 8), UnrealShape.Indirect), (new(4, 11), UnrealShape.Flat), (new(4, 20), UnrealShape.Chunked)]
        ),
    ];

    /// <summary>
    ///     Gets the layout that a minor version uses for each type with several layouts.
    /// </summary>
    /// <param name="minor">The minor version, such as 4.19.</param>
    /// <returns>
    ///     The layouts by type or a failure when <paramref name="minor"/> comes before the 1st layout of a type.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UnrealShape>> Of(Version minor)
    {
        Dictionary<string, UnrealShape> shapes = [with(StringComparer.Ordinal)];

        foreach ((string type, var rows) in _table)
        {
            UnrealShape? shape = null;

            foreach ((var from, var rowShape) in rows)
            {
                if (from <= minor)
                {
                    shape = rowShape;
                }
            }

            if (shape is null)
            {
                return UnrealLayoutError.ShapeNotListed(type, minor, rows[0].From);
            }

            shapes[type] = shape.Value;
        }

        return shapes;
    }

    /// <summary>
    ///     Gets the spelling of <paramref name="shape"/> as <c>$shape</c> in the engine library profiles and in the report.
    /// </summary>
    /// <param name="shape">The layout.</param>
    /// <returns>
    ///     The spelling, such as <c>chunked</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shape"/> is not an <see cref="UnrealShape"/> member.</exception>
    public static string NameOf(UnrealShape shape)
    {
        return shape switch
        {
            UnrealShape.Chunked => "chunked",
            UnrealShape.FField => "ffield",
            UnrealShape.Flat => "flat",
            UnrealShape.Indirect => "indirect",
            UnrealShape.Pointers => "pointers",
            UnrealShape.UProperty => "uproperty",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, UnrealSR.FormatShapeInvalid(shape)),
        };
    }
}
