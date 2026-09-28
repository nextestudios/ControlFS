<#
.SYNOPSIS
  Mede o custo do ControlFS (docs/performance.md): tempo até a janela e o primeiro quadro, memória, CPU e threads em
  repouso com a janela ativa e minimizada, e o tamanho dos pacotes. Usado pelo workflow Smoke (modo full).
.DESCRIPTION
  Cada execução: abre o app (--no-onboarding), espera a linha "Primeiro quadro" do startup.log, traz a janela para a
  frente, espera 3 s, mede 10 s em primeiro plano, minimiza (ShowWindow SW_MINIMIZE, que também tira o foco), espera
  3 s e mede mais 10 s. CPU = tempo de processador do processo / tempo de relógio (% de UM núcleo).
  Portátil: a primeira execução usa uma pasta de extração vazia (DOTNET_BUNDLE_EXTRACT_BASE_DIR) = primeira abertura
  real; as demais reaproveitam a extração. Instalado: instala em silêncio numa pasta temporária e desinstala no fim.
#>
param(
    [string] $Portable,
    [string] $Setup,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [int] $Runs = 3,
    [int] $IdleSeconds = 10
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -Namespace ControlFSPerf -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr hWnd);
'@

function Get-LogMs([string] $Log, [string] $Marker) {
    if (-not (Test-Path -LiteralPath $Log)) { return $null }
    $line = Get-Content -LiteralPath $Log | Where-Object { $_ -like "*$Marker (*ms desde o início do processo)*" } | Select-Object -Last 1
    if ($line -match '\((-?\d+) ms desde') { return [int]$Matches[1] }
    return $null
}

function Get-Sample([System.Diagnostics.Process] $P) {
    $P.Refresh()
    [pscustomobject]@{ Cpu = $P.TotalProcessorTime.TotalMilliseconds; At = [Diagnostics.Stopwatch]::GetTimestamp(); Ws = $P.WorkingSet64; Private = $P.PrivateMemorySize64; Threads = $P.Threads.Count }
}

function Get-CpuPercent($A, $B) {
    $wall = ($B.At - $A.At) * 1000.0 / [Diagnostics.Stopwatch]::Frequency
    if ($wall -le 0) { return 0 }
    [math]::Round(100.0 * ($B.Cpu - $A.Cpu) / $wall, 2)
}

function Measure-Run([string] $Exe, [string] $LogDir, [string] $Package, [string] $Run) {
    Remove-Item -LiteralPath $LogDir -Recurse -Force -ErrorAction SilentlyContinue
    $log = Join-Path $LogDir "startup.log"
    $p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -ArgumentList "--no-onboarding" -PassThru
    $null = $p.Handle
    $deadline = (Get-Date).AddSeconds(90)
    $frame = $null
    while ((Get-Date) -lt $deadline -and -not $p.HasExited) {
        $frame = Get-LogMs $log "Primeiro quadro"
        if ($null -ne $frame) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($null -eq $frame) {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        Write-Host "::warning::$Package/$Run não registrou o primeiro quadro"
        return [pscustomobject]@{ Package = $Package; Run = $Run; Error = "sem primeiro quadro" }
    }
    $window = Get-LogMs $log "Janela ativada"
    $p.Refresh()
    for ($i = 0; $i -lt 50 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 100; $p.Refresh() }
    $hwnd = $p.MainWindowHandle
    $null = [ControlFSPerf.Win32]::ShowWindow($hwnd, 9) # SW_RESTORE
    $null = [ControlFSPerf.Win32]::SetForegroundWindow($hwnd)
    Start-Sleep -Seconds 3
    $foreground = [ControlFSPerf.Win32]::GetForegroundWindow() -eq $hwnd
    $a = Get-Sample $p
    Start-Sleep -Seconds $IdleSeconds
    $b = Get-Sample $p
    $null = [ControlFSPerf.Win32]::ShowWindow($hwnd, 6) # SW_MINIMIZE: minimiza e ativa a próxima janela
    Start-Sleep -Seconds 3
    $minimized = [ControlFSPerf.Win32]::IsIconic($hwnd)
    $c = Get-Sample $p
    Start-Sleep -Seconds $IdleSeconds
    $d = Get-Sample $p
    $exited = $p.HasExited
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    $p.WaitForExit(10000) | Out-Null
    Get-ChildItem -LiteralPath $LogDir -Filter *.log -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $OutDir "$($_.BaseName)-$Package-$Run.log") }
    if ($exited) {
        Write-Host "::error::$Package/$Run fechou durante a medição (código 0x$('{0:X8}' -f $p.ExitCode))"
        return [pscustomobject]@{ Package = $Package; Run = $Run; Error = "exited during the idle measurement (0x$('{0:X8}' -f $p.ExitCode))" }
    }
    $row = [pscustomobject]@{
        Package = $Package; Run = $Run
        WindowMs = $window; FirstFrameMs = $frame
        FgCpuPct = Get-CpuPercent $a $b; FgWsMB = [math]::Round($b.Ws / 1MB, 1); FgPrivateMB = [math]::Round($b.Private / 1MB, 1); FgThreads = $b.Threads
        BgCpuPct = Get-CpuPercent $c $d; BgWsMB = [math]::Round($d.Ws / 1MB, 1); BgPrivateMB = [math]::Round($d.Private / 1MB, 1); BgThreads = $d.Threads
        Foreground = $foreground; Minimized = $minimized
    }
    $row
}

function Get-Median($Values) {
    $v = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    if ($v.Count % 2 -eq 1) { return $v[[int][math]::Floor($v.Count / 2)] }
    ($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2
}

$rows = [System.Collections.Generic.List[object]]::new()
$sizes = [System.Collections.Generic.List[object]]::new()

if ($Portable) {
    $sizes.Add([pscustomobject]@{ Package = "portable"; MB = [math]::Round((Get-Item -LiteralPath $Portable).Length / 1MB, 1) })
    $dir = Join-Path $env:RUNNER_TEMP "perf-portable"
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -LiteralPath $Portable -Destination $dir
    $exe = Join-Path $dir (Split-Path -Leaf $Portable)
    $extract = Join-Path $env:RUNNER_TEMP "perf-extract"
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extract
    try {
        $rows.Add((Measure-Run $exe (Join-Path $dir "ControlFS_Data\logs") "portable" "first"))
        for ($i = 1; $i -le $Runs; $i++) { $rows.Add((Measure-Run $exe (Join-Path $dir "ControlFS_Data\logs") "portable" "warm$i")) }
    } finally {
        Remove-Item Env:\DOTNET_BUNDLE_EXTRACT_BASE_DIR -ErrorAction SilentlyContinue
    }
}

if ($Setup) {
    $sizes.Add([pscustomobject]@{ Package = "installer"; MB = [math]::Round((Get-Item -LiteralPath $Setup).Length / 1MB, 1) })
    $dir = Join-Path $env:RUNNER_TEMP "perf-install"
    $p = Start-Process -Wait -PassThru -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$dir"
    if ($p.ExitCode -ne 0) { throw "Instalação falhou (código $($p.ExitCode))" }
    $installedMB = [math]::Round(((Get-ChildItem -LiteralPath $dir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    $sizes.Add([pscustomobject]@{ Package = "installed folder"; MB = $installedMB })
    try {
        $exe = Join-Path $dir "ControlFS.exe"
        $logs = Join-Path $env:LOCALAPPDATA "ControlFS\logs"
        $rows.Add((Measure-Run $exe $logs "installed" "first"))
        for ($i = 1; $i -le $Runs; $i++) { $rows.Add((Measure-Run $exe $logs "installed" "warm$i")) }
    } finally {
        Start-Process -Wait -FilePath (Join-Path $dir "unins000.exe") -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' | Out-Null
    }
}

$summary = foreach ($package in ($rows | Select-Object -ExpandProperty Package -Unique)) {
    $warm = @($rows | Where-Object { $_.Package -eq $package -and $_.Run -like "warm*" -and -not $_.Error })
    [pscustomobject]@{
        Package = "$package (median of $($warm.Count) warm)"; Run = "median"
        WindowMs = Get-Median ($warm.WindowMs); FirstFrameMs = Get-Median ($warm.FirstFrameMs)
        FgCpuPct = Get-Median ($warm.FgCpuPct); FgWsMB = Get-Median ($warm.FgWsMB); FgPrivateMB = Get-Median ($warm.FgPrivateMB); FgThreads = Get-Median ($warm.FgThreads)
        BgCpuPct = Get-Median ($warm.BgCpuPct); BgWsMB = Get-Median ($warm.BgWsMB); BgPrivateMB = Get-Median ($warm.BgPrivateMB); BgThreads = Get-Median ($warm.BgThreads)
    }
}

$columns = "Package", "Run", "WindowMs", "FirstFrameMs", "FgCpuPct", "FgWsMB", "FgPrivateMB", "FgThreads", "BgCpuPct", "BgWsMB", "BgPrivateMB", "BgThreads"
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("### ControlFS performance ($([Environment]::ProcessorCount) logical CPUs, $IdleSeconds s idle windows)")
[void]$md.AppendLine("")
[void]$md.AppendLine("CPU % = share of ONE core. Fg = window active in the foreground; Bg = window minimized (inactive).")
[void]$md.AppendLine("")
[void]$md.AppendLine("| " + ($columns -join " | ") + " |")
[void]$md.AppendLine("|" + (($columns | ForEach-Object { "---" }) -join "|") + "|")
foreach ($r in @($rows) + @($summary)) {
    if ($r.PSObject.Properties["Error"] -and $r.Error) { [void]$md.AppendLine("| $($r.Package) | $($r.Run) | $($r.Error) |"); continue }
    [void]$md.AppendLine("| " + (($columns | ForEach-Object { $r.$_ }) -join " | ") + " |")
}
[void]$md.AppendLine("")
[void]$md.AppendLine("| Package | Size (MB) |")
[void]$md.AppendLine("|---|---|")
foreach ($s in $sizes) { [void]$md.AppendLine("| $($s.Package) | $($s.MB) |") }
$text = $md.ToString()
Set-Content -LiteralPath (Join-Path $OutDir "performance.md") -Value $text -Encoding utf8
@{ runs = $rows; summary = $summary; sizes = $sizes } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir "performance.json") -Encoding utf8
Write-Host $text
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $text }
$rows | Where-Object { -not ($_.PSObject.Properties["Error"] -and $_.Error) -and (-not $_.Foreground -or -not $_.Minimized) } |
    ForEach-Object { Write-Host "::warning::$($_.Package)/$($_.Run): foreground=$($_.Foreground) minimized=$($_.Minimized) (measurement conditions not met)" }
$failed = @($rows | Where-Object { $_.PSObject.Properties["Error"] -and $_.Error })
if ($failed.Count -gt 0) { Write-Host "::error::$($failed.Count) execução(ões) sem medição completa (ver a tabela)"; exit 1 }
