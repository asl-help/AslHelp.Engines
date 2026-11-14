# escape=`

# Builds the image the Unreal Engine 4 and 5 players build in. Layers are cached, so a stopped build continues at the 1st missing layer.
# Pin the base by digest, because the ltsc2025 tag moves with every Windows update and the image would change under the same tag.
FROM mcr.microsoft.com/windows/server:ltsc2025@sha256:c241b18ec3d59e82992722b473fa19e290fd27cb7819fe69a0f4ad31130a245a
SHELL ["powershell", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

COPY fetch.ps1 cleanup.ps1 vs-install.ps1 C:/setup/

# DirectX June 2010: the cook imports D3DCOMPILER_43 and UHT imports XINPUT1_3.
RUN C:\setup\fetch.ps1 `
        -Url 'https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe' `
        -Sha256 '053F76DCBB28802E23341B6A787E3B0791C0FA5C8D4D011B1044172DBF89C73B' `
        -Out C:\setup\dl\dx.exe; `
    $extract = Start-Process C:\setup\dl\dx.exe -ArgumentList '/Q', '/T:C:\setup\dl\dx' -Wait -PassThru; `
    if ($extract.ExitCode -ne 0) { throw ('extract exit: ' + $extract.ExitCode) }; `
    $setup = Start-Process C:\setup\dl\dx\DXSETUP.exe -ArgumentList '/silent' -Wait -PassThru; `
    if ($setup.ExitCode -ne 0) { throw ('dxsetup exit: ' + $setup.ExitCode) }; `
    if (-not (Test-Path C:\Windows\System32\D3DCompiler_43.dll)) { throw 'D3DCompiler_43.dll missing' }; `
    C:\setup\cleanup.ps1

# VS2017 Build Tools 15.9: VC 14.16, v140, Windows 8.1 and 10 SDKs, .NET 4.5 and 4.6.2 targeting packs.
# 19041 is the only Windows 10 SDK of the 15.9 catalog. UAT fails with MSB4036 without NuGet.BuildTools.
RUN C:\setup\vs-install.ps1 `
        -Url 'https://download.visualstudio.microsoft.com/download/pr/abf393e2-d833-4067-bfe1-4fce375d1b5c/79166ebaee58c28f280a7d810af3557a8a3e3f7d2ac298e20504d36d6bf6b3ae/vs_BuildTools.exe' `
        -Sha256 '79166EBAEE58C28F280A7D810AF3557A8A3E3F7D2AC298E20504D36D6BF6B3AE' `
        -InstallPath C:\VS2017 `
        -Components @( `
            'Microsoft.VisualStudio.Workload.VCTools', `
            'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', `
            'Microsoft.VisualStudio.Component.VC.140', `
            'Microsoft.VisualStudio.Component.Windows81SDK', `
            'Microsoft.VisualStudio.Component.Windows10SDK.19041', `
            'Microsoft.Component.VC.Runtime.UCRTSDK', `
            'Microsoft.Component.MSBuild', `
            'Microsoft.VisualStudio.Component.NuGet.BuildTools', `
            'Microsoft.Net.Component.4.5.TargetingPack', `
            'Microsoft.Net.Component.4.6.2.TargetingPack', `
            'Microsoft.Net.Component.4.6.2.SDK' `
        )

# 4.19 UBT finds VS2017 through this value alone.
RUN New-Item -Force 'HKCU:\SOFTWARE\Microsoft\VisualStudio\SxS\VS7' | Out-Null; `
    New-ItemProperty -Force -Path 'HKCU:\SOFTWARE\Microsoft\VisualStudio\SxS\VS7' -Name '15.0' -PropertyType String -Value 'C:\VS2017\' | Out-Null

# .NET Framework 4.8 developer pack: the reference assemblies UBT needs.
RUN C:\setup\fetch.ps1 `
        -Url 'https://download.microsoft.com/download/6/4/2/642ec242-448b-49a1-8371-5d9c202eaa46/NDP48-DevPack-ENU.exe' `
        -Sha256 'B37882CDA5610B291B2A984E4C2270F67A448F79D6B1F78337736C95B35C5A7F' `
        -Out C:\setup\dl\ndp48.exe; `
    $setup = Start-Process C:\setup\dl\ndp48.exe -ArgumentList '/q', '/norestart' -Wait -PassThru; `
    if ($setup.ExitCode -notin 0, 3010) { throw ('devpack exit: ' + $setup.ExitCode) }; `
    if (-not (Test-Path 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll')) { throw 'v4.8 reference assemblies missing' }; `
    C:\setup\cleanup.ps1

# VS2022 Build Tools 17.14: latest VC 14.4x, 14.38, 14.29, Windows 10 and 11 SDKs, .NET 4.8 SDK.
RUN C:\setup\vs-install.ps1 `
        -Url 'https://download.visualstudio.microsoft.com/download/pr/6bd63d7f-1818-4e03-a552-9feb11496c19/5dc747350fb0589f908915c7b5fa52ec22d22b37ae2e5f4964d83519667498b2/vs_BuildTools.exe' `
        -Sha256 '5DC747350FB0589F908915C7B5FA52EC22D22B37AE2E5F4964D83519667498B2' `
        -InstallPath C:\VS2022 `
        -Components @( `
            'Microsoft.VisualStudio.Workload.VCTools', `
            'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', `
            'Microsoft.VisualStudio.Component.VC.14.29.16.11.x86.x64', `
            'Microsoft.VisualStudio.Component.VC.14.38.17.8.x86.x64', `
            'Microsoft.VisualStudio.Component.Windows10SDK.19041', `
            'Microsoft.VisualStudio.Component.Windows11SDK.26100', `
            'Microsoft.Component.MSBuild', `
            'Microsoft.Net.Component.4.8.SDK', `
            'Microsoft.Net.Component.4.8.TargetingPack' `
        )

# VS2013 Update 5 comes after the large layers, so a change to it reruns only itself and the small layers below.
COPY vs2013-install.ps1 vs2013-admin.xml C:/setup/
RUN C:\setup\vs2013-install.ps1 `
        -Url 'https://download.microsoft.com/download/A/A/D/AAD1AA11-FF9A-4B3C-8601-054E89260B78/vs2013.5_ce_enu.iso' `
        -Sha256 '7DE2340D9570895451C5F5D9F2081DB860A4F753E7596E31B2742390A6D0E6BC'

# UBT 4.11 and 4.12 find VS2015 only through the folder of its IDE. Without it they use VS2013,
# which mangles empty template packs unlike the VS2015 engine libraries, so the link fails.
RUN New-Item -Force 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0' | Out-Null; `
    New-ItemProperty -Force -Path 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0' -Name 'InstallDir' -PropertyType String `
        -Value 'C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE' | Out-Null

# VC++ 14.50 x64 runtime: the 5.8 editor stops the cook on the 14.44 runtime that VS2022 brings.
RUN C:\setup\fetch.ps1 `
        -Url 'https://download.visualstudio.microsoft.com/download/pr/78d061c5-9ef3-42ab-98ed-3309d1095dc4/9C02C9ED120F671B900E6B9471D890E3B2414117A7E53F4AE2E855466FA2B7C9/VC_redist.x64.exe' `
        -Sha256 '9C02C9ED120F671B900E6B9471D890E3B2414117A7E53F4AE2E855466FA2B7C9' `
        -Out C:\setup\dl\vc_redist.x64.exe; `
    $setup = Start-Process C:\setup\dl\vc_redist.x64.exe -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru; `
    if ($setup.ExitCode -notin 0, 3010) { throw ('vc_redist exit: ' + $setup.ExitCode) }; `
    $version = [version](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64').Version.TrimStart('v'); `
    if ($version -lt [version]'14.50') { throw ('vc runtime: ' + $version) }; `
    C:\setup\cleanup.ps1
