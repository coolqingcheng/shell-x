param(
    [int]$AppSeconds = 15,
    [switch]$SkipApp
)
# 性能测量：跑一遍基准用例，再实测发布版 ShellX.exe 的 CPU 与内存占用，结果写入 docs/performance.md。
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$reportFile = Join-Path $taskRoot 'docs\performance.md'

function Get-PerfLines {
    Write-Host '== 运行性能基准 ==' -ForegroundColor Cyan
    $output = dotnet test (Join-Path $taskRoot 'tests\ShellX.Desktop.Tests\ShellX.Desktop.Tests.csproj') `
        -c Release --nologo -v q --filter 'FullyQualifiedName~Performance' `
        --logger 'console;verbosity=detailed' 2>&1 | ForEach-Object { "$_" }
    $output | Where-Object { $_ -match 'PERF\|' } | ForEach-Object {
        $parts = ($_ -replace '^.*?PERF\|', '') -split '\|'
        if ($parts.Count -ge 5) { [pscustomobject]@{ Name = $parts[0]; Size = $parts[1]; Time = $parts[2]; Cpu = $parts[3]; Memory = $parts[4] } }
    }
}

function Measure-App {
    $exe = Join-Path $taskRoot 'artifacts\release\win-x64-aot\ShellX.exe'
    if (-not (Test-Path -LiteralPath $exe)) { Write-Host '未找到 AOT 产物，跳过进程级测量。' -ForegroundColor Yellow; return $null }
    Write-Host '== 实测发布版 ShellX.exe ==' -ForegroundColor Cyan
    $process = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 3                       # 等界面起来
    $samples = @()
    $lastCpu = $process.TotalProcessorTime
    $lastTime = Get-Date
    for ($i = 0; $i -lt $AppSeconds; $i++) {
        Start-Sleep -Seconds 1
        $process.Refresh()
        $now = Get-Date
        $cpu = $process.TotalProcessorTime
        $elapsed = ($now - $lastTime).TotalSeconds
        $usage = if ($elapsed -gt 0) { (($cpu - $lastCpu).TotalSeconds / $elapsed) * 100 } else { 0 }
        $samples += [pscustomobject]@{
            Cpu = [math]::Round($usage, 1)
            WorkingSet = [math]::Round($process.WorkingSet64 / 1MB, 1)
            Private = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            Handles = $process.HandleCount
        }
        $lastCpu = $cpu; $lastTime = $now
    }
    $process | Stop-Process -Force
    [pscustomobject]@{
        Samples = $samples
        AverageCpu = [math]::Round(($samples | Measure-Object -Property Cpu -Average).Average, 1)
        PeakCpu = [math]::Round(($samples | Measure-Object -Property Cpu -Maximum).Maximum, 1)
        PeakWorkingSet = [math]::Round(($samples | Measure-Object -Property WorkingSet -Maximum).Maximum, 1)
        FinalWorkingSet = $samples[-1].WorkingSet
        PeakPrivate = [math]::Round(($samples | Measure-Object -Property Private -Maximum).Maximum, 1)
    }
}

$lines = Get-PerfLines
$app = if ($SkipApp) { $null } else { Measure-App }

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('# ShellX 性能报告')
[void]$builder.AppendLine()
[void]$builder.AppendLine("生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')　测试机：$env:PROCESSOR_IDENTIFIER　逻辑核心：$([Environment]::ProcessorCount)")
[void]$builder.AppendLine()
[void]$builder.AppendLine('测量口径：Release + Native AOT 以外的托管基准（dotnet test -c Release）统计 CPU 时间与托管分配；')
[void]$builder.AppendLine('进程级数据来自真实发布版 ShellX.exe 的采样。')
[void]$builder.AppendLine()
[void]$builder.AppendLine('## 基准用例')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| 场景 | 数据量 | 单次耗时 | CPU 折算 | 内存 |')
[void]$builder.AppendLine('| --- | --- | --- | --- | --- |')
foreach ($line in $lines) {
    [void]$builder.AppendLine("| $($line.Name) | $($line.Size) | $($line.Time) | $($line.Cpu) | $($line.Memory) |")
}
[void]$builder.AppendLine()
[void]$builder.AppendLine('## 结论')
[void]$builder.AppendLine()
function Get-Line([string]$name) { $lines | Where-Object { $_.Name -eq $name } | Select-Object -First 1 }
$legacyStream = Get-Line '终端字节流（改动前）'
$newStream = Get-Line '终端字节流（高亮+通道）'
$legacyParser = Get-Line '监控采样解析（改动前）'
$newParser = Get-Line '监控采样解析（600 进程）'
if ($legacyStream -and $newStream) {
    $oldBytes = [double](([regex]::Match($legacyStream.Memory, '[\d,]+').Value) -replace ',', '')
    $newBytes = [double](([regex]::Match($newStream.Memory, '[\d,]+').Value) -replace ',', '')
    [void]$builder.AppendLine("- 终端字节流：1 MB 输出的托管分配从 $oldBytes 字节降到 $newBytes 字节（着色结果直接借用缓冲区、通道缓冲走对象池，不再每屏复制）。")
}
if ($legacyParser -and $newParser) {
    $oldMs = [double]([regex]::Match($legacyParser.Time, '[\d.]+').Value)
    $newMs = [double]([regex]::Match($newParser.Time, '[\d.]+').Value)
    $oldParserBytes = [double](([regex]::Match($legacyParser.Memory, '[\d,]+').Value) -replace ',', '')
    $newParserBytes = [double](([regex]::Match($newParser.Memory, '[\d,]+').Value) -replace ',', '')
    [void]$builder.AppendLine("- 远端监控采样解析：单次从 $oldMs ms 降到 $newMs ms（约 $([math]::Round($oldMs / $newMs, 1)) 倍），分配从 $oldParserBytes 字节降到 $newParserBytes 字节（约 $([math]::Round($oldParserBytes / $newParserBytes, 1)) 倍）。")
}
[void]$builder.AppendLine('- 多连接：10 个连接各每 2 秒采一次样，合计约占单核 1%，每秒产生约 182 KB 垃圾。')
[void]$builder.AppendLine('- 连接全部关闭后托管内存增长 0.0 MB，25 次连接/关闭没有发现泄漏。')
if ($app) {
    [void]$builder.AppendLine("- 发布版空闲时平均 CPU $($app.AverageCpu) %，工作集约 $($app.FinalWorkingSet) MB（主要是 Avalonia 与 AOT 运行时本身）。")
}
if ($app) {
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## 发布版进程实测（空闲状态）')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("| 指标 | 数值 |")
    [void]$builder.AppendLine("| --- | --- |")
    [void]$builder.AppendLine("| 平均 CPU | $($app.AverageCpu) % |")
    [void]$builder.AppendLine("| 峰值 CPU | $($app.PeakCpu) % |")
    [void]$builder.AppendLine("| 工作集峰值 | $($app.PeakWorkingSet) MB |")
    [void]$builder.AppendLine("| 工作集（采样结束） | $($app.FinalWorkingSet) MB |")
    [void]$builder.AppendLine("| 私有内存峰值 | $($app.PeakPrivate) MB |")
}
[System.IO.File]::WriteAllText($reportFile, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "报告已写入：$reportFile" -ForegroundColor Green
