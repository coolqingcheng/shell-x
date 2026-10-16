@echo off
setlocal

rem Build ShellX and package the Windows x64 self-contained single-file executable.
rem Usage:
rem   build.bat
rem   build.bat -Aot
rem Any additional arguments are passed to scripts\publish.ps1.

set "SCRIPT_DIR=%~dp0"
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\publish.ps1" %*
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
    echo.
    echo ShellX build failed with exit code %BUILD_EXIT%.
    endlocal & exit /b %BUILD_EXIT%
)

echo.
echo ShellX single-file package is ready under artifacts\release\.
endlocal & exit /b 0
