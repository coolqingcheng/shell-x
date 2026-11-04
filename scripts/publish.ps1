param([string]$Runtime = 'win-x64', [string]$BuildArtifactsPath = '', [switch]$Aot, [string]$Version = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVariant = $Runtime
if ($Aot) { $taskVariant += '-aot' }
$taskOutput = Join-Path $taskRoot "artifacts\publish\$taskVariant"
$taskProject = Join-Path $taskRoot 'src\ShellX.Desktop\ShellX.Desktop.csproj'
$taskBuildArguments = @()
if ($BuildArtifactsPath) { $taskBuildArguments = @('--artifacts-path', $BuildArtifactsPath) }
if ($Version) { $taskBuildArguments += "-p:ShellXVersion=$Version" }
# Build the normal Release folder first; RID publish only updates its runtime subdirectory.
dotnet build $taskProject -c Release @taskBuildArguments
if ($LASTEXITCODE -ne 0) { throw 'ShellX Release build failed.' }
if ($Aot) {
    dotnet publish $taskProject -c Release -r $Runtime --self-contained true `
        -p:PublishProfile=win-x64-aot -o $taskOutput @taskBuildArguments
} else {
    dotnet publish $taskProject -c Release -r $Runtime --self-contained true `
        -p:PublishAot=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput @taskBuildArguments
}
if ($LASTEXITCODE -ne 0) { throw 'ShellX publish failed.' }
$taskDelivery = Join-Path $taskRoot "artifacts\release\$taskVariant"
New-Item -ItemType Directory -Path $taskDelivery -Force | Out-Null
if ($Aot) {
    $taskExtraFiles = @(Get-ChildItem -LiteralPath $taskOutput -Recurse -File | Where-Object { $_.Extension -ne '.pdb' -and $_.Name -ne 'ShellX.Desktop.exe' })
    if ($taskExtraFiles.Count -gt 0) { throw ('AOT output still contains external dependencies: ' + ($taskExtraFiles.Name -join ', ')) }
}
Copy-Item -LiteralPath (Join-Path $taskOutput 'ShellX.Desktop.exe') -Destination (Join-Path $taskDelivery 'ShellX.exe') -Force
$taskVersion = if ($Version) { $Version } else { (dotnet msbuild $taskProject -getProperty:Version -nologo | Select-Object -Last 1).Trim() }
Write-Host "ShellX version: $taskVersion"
Write-Host "ShellX: $(Join-Path $taskDelivery 'ShellX.exe')"
