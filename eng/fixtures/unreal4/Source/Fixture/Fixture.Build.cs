using UnrealBuildTool;

public class Fixture : ModuleRules
{
    //@if >=4.16
    public Fixture(ReadOnlyTargetRules Target) : base(Target)
    //@else
    public Fixture(TargetInfo Target)
    //@endif
    {
        //@if >=4.15
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        //@else
        //@if >=4.5
        //@if <4.8
        // Use the module's own PCH, because the editor target of these installed engines finds no shared PCH and stops.
        PCHUsage = PCHUsageMode.NoSharedPCHs;
        //@else
        PCHUsage = PCHUsageMode.UseSharedPCHs;
        //@endif
        //@else
        PCHUsage = PCHUsageMode.UseSharedPCHs;
        //@endif
        //@endif
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
        //@if >=4.10
        //@if <4.12
        // Compile without a PCH, because cl 19.00.24247 rejects the one UBT 4.10 and 4.11 create with C4599 on a /I argument.
        MinFilesUsingPrecompiledHeaderOverride = 1000;
        //@endif
        //@endif
        //@if ==4.10
        // List the Windows 8.1 SDK first, because winnt.h of the Windows 10 SDK refuses the 4-byte packing of a 32-bit build.
        if (Target.Platform == UnrealTargetPlatform.Win32)
        {
            PublicSystemIncludePaths.InsertRange(0, new string[]
            {
                @"C:\Program Files (x86)\Windows Kits\8.1\Include\shared",
                @"C:\Program Files (x86)\Windows Kits\8.1\Include\um",
            });
        }
        //@endif
    }
}
