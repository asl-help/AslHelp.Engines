using AslHelp.Engines.Measuring.Cli;
using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using System;

return await CommandLine.RunAsync(args, Console.Out, Console.Error, [new UdkMeasurer(), new UnrealMeasurer()]).ConfigureAwait(false);
