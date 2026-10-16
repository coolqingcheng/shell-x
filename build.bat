@echo off
setlocal

rem Build ShellX as a Windows x64 Native AOT single-file executable by default.
rem Usage:
rem   build.bat
rem   build.bat -BuildArtifactsPath artifacts\isolated-build
rem Pass -Aot explicitly if needed; the script already enables it by default.

set "SCRIPT_DIR=%~dp0"
set "HAS_AOT="
for %%A in (%*) do if /I "%%~A"=="-Aot" set "HAS_AOT=1"

if defined HAS_AOT (
    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\publish.ps1" %*
) else (
    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\publish.ps1" -Aot %*
)
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
    echo.
    echo ShellX Native AOT build failed with exit code %BUILD_EXIT%.
    endlocal & exit /b %BUILD_EXIT%
)

echo.
echo ShellX Native AOT single-file package is ready under artifacts\release\win-x64-aot\ShellX.exe.
endlocal & exit /b 0
