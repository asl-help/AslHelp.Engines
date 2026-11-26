using System.Text.Json.Nodes;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Provides changes to 1 member of a JSON document, so a test can start from a full profile and change only what it checks.
/// </summary>
internal static class JsonEdits
{
    /// <summary>
    ///     Removes the member at <paramref name="path"/>.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="path">The keys from the root to the member.</param>
    /// <returns>
    ///     The changed document.
    /// </returns>
    public static string Remove(string json, params string[] path)
    {
        var root = JsonNode.Parse(json)!;
        _ = Parent(root, path).AsObject().Remove(path[^1]);

        return root.ToJsonString();
    }

    /// <summary>
    ///     Sets the member at <paramref name="path"/> to <paramref name="value"/>, adding it when it's missing.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="value">The JSON text of the new value.</param>
    /// <param name="path">The keys from the root to the member.</param>
    /// <returns>
    ///     The changed document.
    /// </returns>
    public static string Set(string json, string value, params string[] path)
    {
        var root = JsonNode.Parse(json)!;
        Parent(root, path)[path[^1]] = JsonNode.Parse(value);

        return root.ToJsonString();
    }

    private static JsonNode Parent(JsonNode root, string[] path)
    {
        var node = root;

        for (int i = 0; i < path.Length - 1; i++)
        {
            node = node[path[i]]!;
        }

        return node;
    }
}
