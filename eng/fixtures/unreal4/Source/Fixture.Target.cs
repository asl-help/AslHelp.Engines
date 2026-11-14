using UnrealBuildTool;
//@if <4.16
using System.Collections.Generic;
//@endif

public class FixtureTarget : TargetRules
{
    //@if >=4.16
    public FixtureTarget(TargetInfo Target) : base(Target)
    //@else
    public FixtureTarget(TargetInfo Target)
    //@endif
    {
        Type = TargetType.Game;
        //@if >=4.24
        DefaultBuildSettings = BuildSettingsVersion.Latest;
        //@endif
        //@if >=5.1
        IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
        //@endif
        //@if >=4.16
        ExtraModuleNames.Add("Fixture");
        //@endif
        //@if >=4.15
        //@if <4.20
        // Pick VS2017, because the build tool uses VS2015 when it finds it.
        WindowsPlatform.Compiler = WindowsCompiler.VisualStudio2017;
        //@endif
        //@endif
        //@if >=4.20
        //@if <4.22
        // Pin the last VS2019 toolset, because 14.40 and up turn C4865 into an error in the engine headers. UBT 4.22 and 4.23 look
        // for the pin in VS2017 installs only, so they take the VS2017 toolset of the image instead.
        WindowsPlatform.CompilerVersion = "14.29.30133";
        //@endif
        //@endif
        //@if >=4.16
        //@if <4.19
        // Define the intrinsic that wchar.h of the current universal CRT calls, because the 32-bit compiler lacks it.
        if (Target.Platform == UnrealTargetPlatform.Win32)
        {
            GlobalDefinitions.Add("_mm_loadu_si64(p)=_mm_loadl_epi64((__m128i const*)(p))");
        }
        //@endif
        //@endif
    }
    //@if <4.16

    public override void SetupBinaries(
        TargetInfo Target,
        ref List<UEBuildBinaryConfiguration> OutBuildBinaryConfigurations,
        ref List<string> OutExtraModuleNames)
    {
        OutExtraModuleNames.Add("Fixture");
    }
    //@endif
    //@if ==4.15

    // Defines the intrinsic here, because target rules on 4.15 don't have GlobalDefinitions.
    public override void SetupGlobalEnvironment(
        TargetInfo Target,
        ref LinkEnvironmentConfiguration OutLinkEnvironmentConfiguration,
        ref CPPEnvironmentConfiguration OutCPPEnvironmentConfiguration)
    {
        if (Target.Platform == UnrealTargetPlatform.Win32)
        {
            OutCPPEnvironmentConfiguration.Definitions.Add("_mm_loadu_si64(p)=_mm_loadl_epi64((__m128i const*)(p))");
        }
    }
    //@endif
}
