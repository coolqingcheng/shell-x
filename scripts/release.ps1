param(
    [string]$Version = '',
    [string]$Message = '',
    [string]$Branch = '',
    [switch]$NoPush,
    [switch]$NoTag
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $taskRoot 'VERSION'

function Get-CurrentVersion {
    if (-not (Test-Path -LiteralPath $versionFile)) { return '1.0.0' }
    return (Get-Content -LiteralPath $versionFile -Raw).Trim()
}

function Get-NextPatchVersion([string]$current) {
    if ($current -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "无法解析当前版本号：$current" }
    $parts = @($Matches[1], $Matches[2], ([int]$Matches[3] + 1))
    return ($parts -join '.')
}

$currentVersion = Get-CurrentVersion
if ($Version) {
    $nextVersion = $Version.Trim().TrimStart('v', 'V')
} else {
    $nextVersion = Get-NextPatchVersion $currentVersion
}
Write-Host "版本号：$currentVersion -> $nextVersion"

# 1) 写入版本文件（UTF-8 无 BOM，末尾换行）
[System.IO.File]::WriteAllText($versionFile, "$nextVersion`n", (New-Object System.Text.UTF8Encoding($false)))

# 2) 本地 AOT 构建
& (Join-Path $PSScriptRoot 'publish.ps1') -Aot -Version $nextVersion
if ($LASTEXITCODE -ne 0) { throw 'ShellX AOT 构建失败。' }
$artifact = Join-Path $taskRoot 'artifacts\release\win-x64-aot\ShellX.exe'
if (-not (Test-Path -LiteralPath $artifact)) { throw '未找到 AOT 产物。' }
$fileVersion = (Get-Item -LiteralPath $artifact).VersionInfo.FileVersion
Write-Host "产物版本：$fileVersion"
if ($fileVersion -notlike "$nextVersion*") { throw "产物版本 $fileVersion 与目标版本 $nextVersion 不一致。" }

# 3) 提交并推送
if (-not $Branch) { $Branch = (git -C $taskRoot rev-parse --abbrev-ref HEAD).Trim() }
git -C $taskRoot add -A
if (-not $Message) { $Message = "chore(release): 发布 v$nextVersion" }
$messageFile = Join-Path ([System.IO.Path]::GetTempPath()) 'shellx-release-message.txt'
[System.IO.File]::WriteAllText($messageFile, $Message, (New-Object System.Text.UTF8Encoding($false)))
try {
    git -C $taskRoot commit -F $messageFile
    if ($LASTEXITCODE -ne 0) { throw '提交失败。' }
} finally {
    [System.IO.File]::Delete($messageFile)
}
if (-not $NoPush) {
    git -C $taskRoot push origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "推送分支 $Branch 失败。" }
}

# 4) 打标签并推送，触发 GitHub Release 工作流
if (-not $NoTag) {
    $tag = "v$nextVersion"
    if (git -C $taskRoot tag -l $tag) { throw "标签 $tag 已存在。" }
    git -C $taskRoot tag -a $tag -m $tag
    if ($LASTEXITCODE -ne 0) { throw "创建标签 $tag 失败。" }
    if (-not $NoPush) {
        git -C $taskRoot push origin $tag
        if ($LASTEXITCODE -ne 0) { throw "推送标签 $tag 失败。" }
    }
    Write-Host "已推送标签 $tag，GitHub 正在编译安装包。"
    Write-Host "Release 地址：https://github.com/coolqingcheng/shell-x/releases/tag/$tag"
}

Write-Host "完成：v$nextVersion"
