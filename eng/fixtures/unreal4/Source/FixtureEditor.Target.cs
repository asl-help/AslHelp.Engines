using UnrealBuildTool;
//@if <4.16
using System.Collections.Generic;
//@endif

public class FixtureEditorTarget : TargetRules
{
    //@if >=4.16
    public FixtureEditorTarget(TargetInfo Target) : base(Target)
    //@else
    public FixtureEditorTarget(TargetInfo Target)
    //@endif
    {
        Type = TargetType.Editor;
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
}
