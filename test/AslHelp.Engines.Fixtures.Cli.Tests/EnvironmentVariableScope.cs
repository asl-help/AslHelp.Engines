using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Saves environment variables and puts their values back on dispose, because the tests share 1 process environment.
/// </summary>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _saved = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class and saves the values of the variables.
    /// </summary>
    /// <param name="names">The names of the variables.</param>
    public EnvironmentVariableScope(params IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
        }
    }

    /// <summary>
    ///     Puts the saved values back.
    /// </summary>
    public void Dispose()
    {
        foreach ((string name, string? value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
