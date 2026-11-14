@echo off
rem Copies the runtime DLLs of the image into C:\o\runtimes\x64 and C:\o\runtimes\x86, for engines without AppLocalDependencies.
rem The build copies only the DLLs a player imports from there. VS2013 players import the 2013 CRT, VS2015 players the 2015
rem one, which the newest CRT of the image serves.
rem Mounts as in editor.bat, only C:\o is used.
set crt120=C:\Program Files (x86)\Microsoft Visual Studio 12.0\VC\redist
rem Take the newest MSVC folder by name, because a VS2022 update replaces the folder of the version before.
set crt140=
for /f "delims=" %%V in ('dir /b /ad /o:n "C:\VS2022\VC\Redist\MSVC\14.*"') do set crt140=C:\VS2022\VC\Redist\MSVC\%%V
if not defined crt140 exit /b 1
mkdir C:\o\runtimes\x64 C:\o\runtimes\x86 2>nul
copy /y "%crt120%\x64\Microsoft.VC120.CRT\*.dll" C:\o\runtimes\x64 || exit /b 1
copy /y "%crt120%\x86\Microsoft.VC120.CRT\*.dll" C:\o\runtimes\x86 || exit /b 1
copy /y "%crt140%\x64\Microsoft.VC143.CRT\*.dll" C:\o\runtimes\x64 || exit /b 1
copy /y "%crt140%\x86\Microsoft.VC143.CRT\*.dll" C:\o\runtimes\x86 || exit /b 1
for %%D in (xinput1_3.dll X3DAudio1_7.dll XAPOFX1_5.dll XAudio2_7.dll d3dx11_43.dll D3DCompiler_43.dll) do (
  copy /y C:\Windows\System32\%%D C:\o\runtimes\x64 || exit /b 1
  copy /y C:\Windows\SysWOW64\%%D C:\o\runtimes\x86 || exit /b 1
)
exit /b 0
