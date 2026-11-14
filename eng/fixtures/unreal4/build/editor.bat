@echo off
rem Builds the editor target, which the cook needs.
rem Mounts: C:\e engine, C:\p project, C:\o out, C:\l logs, C:\r these scripts (read-only).
rem Env: FIXTURE_EDITOR_FLAGS, such as -waitmutex. FIXTURE_PATH_PREPEND, a folder to put first on PATH (optional).
rem By hand:
rem   docker run --rm -v <engine>:C:\e -v <project>:C:\p -v <out>:C:\o -v <logs>:C:\l -v <this folder>:C:\r:ro
rem     -e FIXTURE_EDITOR_FLAGS=-waitmutex asl-help/unreal4-build:<hash> cmd /c C:\r\editor.bat
set uebp_LogFolder=C:/l/uat-editor
set ForcePrecompiledUAT=1
if defined FIXTURE_PATH_PREPEND set "PATH=%FIXTURE_PATH_PREPEND%;%PATH%"
rem Sets up the bundled dotnet, because Build.bat and RunUAT.bat of the installed 5.0 and 5.1 skip GetDotnetPath.bat.
if exist C:\e\Engine\Build\BatchFiles\GetDotnetPath.bat call C:\e\Engine\Build\BatchFiles\GetDotnetPath.bat
call C:\e\Engine\Build\BatchFiles\Build.bat FixtureEditor Win64 Development -project=C:\p\Fixture.uproject %FIXTURE_EDITOR_FLAGS%
exit /b %ERRORLEVEL%
