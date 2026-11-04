param(
    [string]$Version = '',
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot

function Resolve-InnoCompiler {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    foreach ($candidate in $candidates) { return $candidate }
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

if (-not $Version) {
    $props = Get-Content (Join-Path $taskRoot 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
}
if (-not $Version) { throw '无法确定版本号，请使用 -Version 指定。' }
$Version = $Version.Trim().TrimStart('v', 'V')

$compiler = Resolve-InnoCompiler
if (-not $compiler) {
    throw '未找到 Inno Setup 6 编译器 (ISCC.exe)。请先安装：winget install JRSoftware.InnoSetup'
}

$script = Join-Path $taskRoot 'scripts\installer\ShellX.iss'
$payload = Join-Path $taskRoot "artifacts\release\$Runtime-aot\ShellX.exe"
if (-not (Test-Path -LiteralPath $payload)) {
    throw "未找到 AOT 产物 $payload，请先运行 build.bat 编译。"
}

Write-Host "ShellX 安装包版本：$Version"
& $compiler "/DAppVersion=$Version" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败，退出码 $LASTEXITCODE。" }
$installer = Join-Path $taskRoot "artifacts\installer\ShellX-Setup-$Version.exe"
Write-Host "ShellX 安装包：$installer"