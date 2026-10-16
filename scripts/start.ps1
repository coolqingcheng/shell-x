param([switch]$Aot)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVariant = 'win-x64'
if ($Aot) { $taskVariant += '-aot' }
$taskRelease = Join-Path $taskRoot "artifacts\release\$taskVariant\ShellX.exe"
if (Test-Path -LiteralPath $taskRelease) { Start-Process -FilePath $taskRelease -WindowStyle Hidden }
elseif ($Aot) { throw 'Native AOT package not found. Run scripts/publish.ps1 -Aot first.' }
else {
    dotnet run --project (Join-Path $taskRoot 'src\ShellX.Desktop\ShellX.Desktop.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'ShellX failed to start.' }
}
