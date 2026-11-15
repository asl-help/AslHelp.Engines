@echo off
rem Builds 1 player, cooks or reuses the cooked content, then stages, paks and archives it into C:\o\<tag>.
rem Args: platform (Win64 or Win32), configuration (Shipping or Development), tag (such as win-x64-shipping),
rem -cook or -skipcook.
rem Mounts and FIXTURE_PATH_PREPEND as in editor.bat. Env: FIXTURE_PACKAGE_FLAGS, such as -rocket -nobootstrapexe.
rem By hand, after editor.bat:
rem   docker run --rm -v <engine>:C:\e -v <project>:C:\p -v <out>:C:\o -v <logs>:C:\l -v <this folder>:C:\r:ro
rem     asl-help/unreal4-build:<hash> cmd /c C:\r\package.bat Win64 Shipping win-x64-shipping -cook
set uebp_LogFolder=C:/l/uat-package-%3
set ForcePrecompiledUAT=1
if defined FIXTURE_PATH_PREPEND set "PATH=%FIXTURE_PATH_PREPEND%;%PATH%"
rem Sets up the bundled dotnet, because Build.bat and RunUAT.bat of the installed 5.0 and 5.1 skip GetDotnetPath.bat.
if exist C:\e\Engine\Build\BatchFiles\GetDotnetPath.bat call C:\e\Engine\Build\BatchFiles\GetDotnetPath.bat
call C:\e\Engine\Build\BatchFiles\RunUAT.bat BuildCookRun -project=C:\p\Fixture.uproject -noP4 -platform=%1 -clientconfig=%2 ^
  -build %4 -stage -pak -package -archive -archivedirectory=C:\o\%3 -nocompile -utf8output %FIXTURE_PACKAGE_FLAGS%
exit /b %ERRORLEVEL%
