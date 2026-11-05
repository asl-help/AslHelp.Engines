using AslHelp.Engines.Fixtures.Cli;
using System;

return await CommandLine.RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);
