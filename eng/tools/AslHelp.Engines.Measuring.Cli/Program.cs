using AslHelp.Engines.Measuring.Cli;
using AslHelp.Engines.Measuring.Cli.Unreal3;
using System;

return await CommandLine.RunAsync(args, Console.Out, Console.Error, [new UdkMeasurer()]).ConfigureAwait(false);
