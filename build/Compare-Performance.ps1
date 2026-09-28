<#
.SYNOPSIS
  Compara o custo do ControlFS (exe portátil) com o Explorador de Arquivos do Windows e o Files (files.community)
  abrindo a MESMA pasta, no mesmo runner (docs/performance.md). Usado pelo workflow Smoke (modo full); roda em pwsh.
.DESCRIPTION
  Cenários: (a) pasta com poucos arquivos; (b) pasta com 5000 arquivos pequenos. Cada app: uma abertura de aquecimento
  (descartada) e -Runs medidas; a tabela usa a mediana. O cenário (c), "entrar numa subpasta e voltar", não é medido:
  não dá para automatizar a navegação da mesma forma nos três apps (o ControlFS é feito para controle/teclado próprio, o
  Files desenha o próprio conteúdo) e não estimamos nada.

  Método por execução: abre o app na pasta; "tempo até a janela" = do início do comando até existir uma janela visível
  (mesmo detector nos três); espera -SettleSeconds; mede -IdleSeconds: CPU (tempo de processador ÷ tempo de relógio, % de
  UM núcleo), conjunto de trabalho, bytes privados (PrivateMemorySize64 = memória comprometida) e threads.

  Somam-se TODOS os processos do app:
    - ControlFS: o processo e seus descendentes.
    - Files: todo processo cujo executável está na pasta do pacote (ou nome Files*.exe) e seus descendentes.
    - Explorador: as janelas de pasta rodam DENTRO do processo do shell (explorer.exe), então mede-se a diferença do
      conjunto de processos explorer.exe entre "sem a janela" (linha de base medida na hora, antes de abrir) e "com a
      janela aberta"; também depois de fechá-la. CPU do Explorador = taxa com a janela - taxa da linha de base.

  ControlFS abre na pasta pelo settings.json da pasta de dados (abas restauradas: a aba ativa é a pasta do cenário e a
  outra é a pasta pequena), sem verificar atualizações e sem boas-vindas; o ControlFS não tem argumento de pasta.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Portable,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [int] $Runs = 3,
    [int] $IdleSeconds = 10,
    [int] $SettleSeconds = 3,
    [int] $BigFiles = 5000
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class CmpWin32 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    public static string Title(IntPtr h) { var t = new StringBuilder(512); GetWindowText(h, t, 512); return t.ToString(); }
    // Janela de pasta do Explorador: classe CabinetWClass com o nome da pasta no título.
    public static IntPtr FindExplorerWindow(string titlePart) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            if (c.ToString() != "CabinetWClass") return true;
            if (Title(h).IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }
    // Janela principal visível (sem dono, com tamanho de janela de verdade) de um dos processos.
    public static IntPtr FindTopWindow(int[] pids) {
        var set = new HashSet<int>(pids);
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            if (GetWindow(h, 4) != IntPtr.Zero) return true; // GW_OWNER
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (!set.Contains((int)pid)) return true;
            RECT r; if (!GetWindowRect(h, out r) || r.R - r.L < 200 || r.B - r.T < 150) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Get-ProcTable { @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath) }

function Get-Descendants([int[]] $Roots, $Table) {
    $set = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($r in $Roots) { [void]$set.Add($r) }
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($row in $Table) { if ($set.Contains([int]$row.ParentProcessId) -and $set.Add([int]$row.ProcessId)) { $grew = $true } }
    }
    @($set)
}

function Get-GroupSample([int[]] $Pids) {
    $cpu = @{}; $ws = 0L; $priv = 0L; $thr = 0; $n = 0
    foreach ($id in $Pids) {
        try {
            $p = [Diagnostics.Process]::GetProcessById($id)
            $cpu[$id] = $p.TotalProcessorTime.TotalMilliseconds
            $ws += $p.WorkingSet64; $priv += $p.PrivateMemorySize64; $thr += $p.Threads.Count; $n++
        } catch { }
    }
    [pscustomobject]@{ Cpu = $cpu; Ws = $ws; Private = $priv; Threads = $thr; Count = $n; At = [Diagnostics.Stopwatch]::GetTimestamp() }
}

# CPU do grupo entre duas amostras, em % de UM núcleo (processo que nasceu no intervalo conta o total dele).
function Get-CpuPercent($A, $B) {
    $wall = ($B.At - $A.At) * 1000.0 / [Diagnostics.Stopwatch]::Frequency
    if ($wall -le 0) { return 0 }
    $used = 0.0
    foreach ($id in $B.Cpu.Keys) { $used += if ($A.Cpu.ContainsKey($id)) { [math]::Max(0, $B.Cpu[$id] - $A.Cpu[$id]) } else { $B.Cpu[$id] } }
    [math]::Round(100.0 * $used / $wall, 2)
}

function Get-Median($Values) {
    $v = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    if ($v.Count % 2 -eq 1) { return $v[[int][math]::Floor($v.Count / 2)] }
    ($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2
}

function Stop-Pids([int[]] $Pids) {
    foreach ($id in $Pids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and @($Pids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count -gt 0) { Start-Sleep -Milliseconds 200 }
}

# ---- pastas de teste -------------------------------------------------------------------------------------------
$base = Join-Path $temp "perf-compare"
Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
$small = Join-Path $base "perf-small"
$big = Join-Path $base "perf-big"
foreach ($d in $small, (Join-Path $small "sub"), $big, (Join-Path $big "sub")) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
1..20 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $small ("note{0:D2}.txt" -f $_)), "small file $_`n") }
1..20 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $small ("sub\inner{0:D2}.txt" -f $_)), "inner $_`n") }
1..$BigFiles | ForEach-Object { [IO.File]::WriteAllText((Join-Path $big ("file{0:D4}.txt" -f $_)), ("x" * 1024)) }
1..20 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $big ("sub\inner{0:D2}.txt" -f $_)), "inner $_`n") }
Write-Host "Pastas: $small ($((Get-ChildItem $small -Recurse -File).Count) arquivos), $big ($((Get-ChildItem $big -File).Count) arquivos + subpasta)"

# ---- ControlFS -----------------------------------------------------------------------------------------------------
$cfsDir = Join-Path $temp "cmp-portable"
Remove-Item -LiteralPath $cfsDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $cfsDir | Out-Null
Copy-Item -LiteralPath $Portable -Destination $cfsDir
$cfsExe = Join-Path $cfsDir (Split-Path -Leaf $Portable)
$cfsData = Join-Path $cfsDir "ControlFS_Data"
$extract = Join-Path $temp "cmp-extract"
Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extract

# ---- Files: instalação (winget; senão o App Installer do CDN oficial); qualquer falha vira "não medido" ------------
function Get-FilesPackage { Get-AppxPackage | Where-Object { $_.Name -match 'FilesUWP|FilesCommunity' } | Select-Object -First 1 }
$filesNote = $null
$filesPkg = $null
try {
    $filesPkg = Get-FilesPackage
    if (-not $filesPkg) {
        $log = [System.Collections.Generic.List[string]]::new()
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            Write-Host "winget install FilesCommunity.Files"
            $out = & winget install --id FilesCommunity.Files -e --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-String
            Write-Host $out
            $log.Add("winget (id FilesCommunity.Files) exit=$LASTEXITCODE")
        } else { $log.Add("winget indisponível no runner") }
        $filesPkg = Get-FilesPackage
    }
    if (-not $filesPkg) {
        foreach ($url in "https://cdn.files.community/files/stable/Files.Package.appinstaller", "https://files.community/appinstallers/Files.stable.appinstaller") {
            try {
                Write-Host "Add-AppxPackage -AppInstallerFile $url"
                Add-AppxPackage -AppInstallerFile $url -ErrorAction Stop
                $filesPkg = Get-FilesPackage
                if ($filesPkg) { $log.Add("App Installer $url ok"); break }
            } catch { $log.Add("App Installer $url falhou: $($_.Exception.Message)"); Write-Host "::warning::Files: $($_.Exception.Message)" }
        }
    }
    if (-not $filesPkg) { $filesNote = "não instalou: " + ($log -join "; ") }
} catch { $filesNote = "erro na instalação: $($_.Exception.Message)" }
Write-Host ("Pacotes com 'Files' no nome: " + ((Get-AppxPackage | Where-Object Name -like '*Files*' | ForEach-Object { "$($_.Name) $($_.Version)" }) -join "; "))
if ($filesPkg) { Write-Host "Files: $($filesPkg.PackageFullName) em $($filesPkg.InstallLocation)" } else { Write-Host "::warning::Files: $filesNote" }

# ---- definição de cada app -----------------------------------------------------------------------------------------
$script:ctx = @{}
$apps = [ordered]@{}

$apps["ControlFS"] = @{
    Prepare = {
        param($folder)
        New-Item -ItemType Directory -Force -Path $cfsData | Out-Null
        Remove-Item -LiteralPath (Join-Path $cfsData "logs") -Recurse -Force -ErrorAction SilentlyContinue
        $json = [ordered]@{ AutoCheckUpdates = $false; OnboardingCompleted = $true; RestoreTabs = $true; OpenTabs = @($folder, $small); ActiveOpenTab = 0 } | ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $cfsData "settings.json"), $json)
    }
    Launch = { param($folder) $p = Start-Process -FilePath $cfsExe -WorkingDirectory $cfsDir -ArgumentList "--no-onboarding" -PassThru; $null = $p.Handle; $script:ctx.Root = $p.Id }
    Group = { Get-Descendants @($script:ctx.Root) (Get-ProcTable) }
    Window = { param($folder, $pids) [CmpWin32]::FindTopWindow([int[]]$pids) }
    Cleanup = { param($pids) Stop-Pids $pids }
}

$apps["Explorer"] = @{
    Prepare = { param($folder) }
    Launch = { param($folder) $script:ctx.Leaf = Split-Path -Leaf $folder; Start-Process -FilePath "explorer.exe" -ArgumentList "`"$folder`"" }
    Group = { @(Get-Process -Name explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
    Window = { param($folder, $pids) [CmpWin32]::FindExplorerWindow((Split-Path -Leaf $folder)) }
    Cleanup = { param($pids) }
}

if ($filesPkg) {
    $filesRoot = $filesPkg.InstallLocation
    $apps["Files"] = @{
        Prepare = { param($folder) }
        Launch = {
            param($folder)
            $alias = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\files.exe"
            if (Test-Path $alias) { Start-Process -FilePath $alias -ArgumentList "`"$folder`"" }
            else { Start-Process "shell:AppsFolder\$($filesPkg.PackageFamilyName)!App" }
        }
        Group = {
            $t = Get-ProcTable
            $roots = @($t | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($filesRoot, [StringComparison]::OrdinalIgnoreCase)) -or $_.Name -like 'Files*.exe' } | ForEach-Object { [int]$_.ProcessId })
            if ($roots.Count -eq 0) { @() } else { Get-Descendants $roots $t }
        }
        Window = { param($folder, $pids) if (@($pids).Count -eq 0) { [IntPtr]::Zero } else { [CmpWin32]::FindTopWindow([int[]]$pids) } }
        Cleanup = { param($pids) Stop-Pids $pids; Start-Sleep -Seconds 1; Stop-Pids @(& $apps["Files"].Group) }
    }
}

function Measure-One([string] $App, [string] $Scenario, [string] $Folder, [string] $Run) {
    $def = $apps[$App]
    $isShell = $App -eq "Explorer"
    $result = [ordered]@{ App = $App; Scenario = $Scenario; Run = $Run }
    $pids = @()
    try {
        & $def.Prepare $Folder
        $baseA = $null; $baseB = $null
        if ($isShell) {
            $baseA = Get-GroupSample (& $def.Group)
            Start-Sleep -Seconds $IdleSeconds
            $baseB = Get-GroupSample (& $def.Group)
            $result.ShellProcsBefore = $baseB.Count
        }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & $def.Launch $Folder
        $hwnd = [IntPtr]::Zero
        while ($sw.Elapsed.TotalSeconds -lt 60) {
            $pids = @(& $def.Group)
            $hwnd = & $def.Window $Folder $pids
            if ($hwnd -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 25
        }
        if ($hwnd -eq [IntPtr]::Zero) { throw "sem janela em 60 s" }
        $result.WindowMs = [int]$sw.ElapsedMilliseconds
        $result.Title = [CmpWin32]::Title($hwnd)
        $null = [CmpWin32]::ShowWindow($hwnd, 9)
        $null = [CmpWin32]::SetForegroundWindow($hwnd)
        Start-Sleep -Seconds $SettleSeconds
        $a = Get-GroupSample (& $def.Group)
        Start-Sleep -Seconds $IdleSeconds
        $pids = @(& $def.Group)
        $b = Get-GroupSample $pids
        $cpu = Get-CpuPercent $a $b
        $result.Procs = $b.Count
        $result.AbsWsMB = [math]::Round($b.Ws / 1MB, 1); $result.AbsPrivateMB = [math]::Round($b.Private / 1MB, 1)
        if ($isShell) {
            $result.CpuPct = [math]::Round($cpu - (Get-CpuPercent $baseA $baseB), 2)
            $result.AbsCpuPct = $cpu
            $result.WsMB = [math]::Round(($b.Ws - $baseB.Ws) / 1MB, 1)
            $result.PrivateMB = [math]::Round(($b.Private - $baseB.Private) / 1MB, 1)
            $result.Threads = $b.Threads - $baseB.Threads
            $result.Procs = $b.Count - $baseB.Count
            $null = [CmpWin32]::PostMessage($hwnd, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
            $deadline = (Get-Date).AddSeconds(15)
            while ((Get-Date) -lt $deadline -and [CmpWin32]::FindExplorerWindow((Split-Path -Leaf $Folder)) -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
            Start-Sleep -Seconds $SettleSeconds
            $c = Get-GroupSample (& $def.Group)
            $result.AfterCloseWsMB = [math]::Round(($c.Ws - $baseB.Ws) / 1MB, 1)
            $result.AfterClosePrivateMB = [math]::Round(($c.Private - $baseB.Private) / 1MB, 1)
        } else {
            $result.CpuPct = $cpu
            $result.WsMB = $result.AbsWsMB; $result.PrivateMB = $result.AbsPrivateMB; $result.Threads = $b.Threads
        }
        if ($App -eq "ControlFS") {
            $log = Join-Path $cfsData "logs\startup.log"
            if (Test-Path -LiteralPath $log) {
                $line = Get-Content -LiteralPath $log | Where-Object { $_ -like "*Primeiro quadro (*ms desde o início do processo)*" } | Select-Object -Last 1
                if ($line -match '\((-?\d+) ms desde') { $result.FirstFrameMs = [int]$Matches[1] }
                Copy-Item -LiteralPath $log -Destination (Join-Path $OutDir "startup-$Scenario-$Run.log") -ErrorAction SilentlyContinue
            }
        }
    } catch {
        $result.Error = $_.Exception.Message
        Write-Host "::warning::$App/$Scenario/$Run : $($result.Error)"
    } finally {
        try { $pids = @(& $def.Group) } catch { }
        & $def.Cleanup $pids
        Start-Sleep -Seconds 2
    }
    [pscustomobject]$result
}

$scenarios = @(
    @{ Id = "a"; Label = "small folder (40 files)"; Folder = $small },
    @{ Id = "b"; Label = "$BigFiles small files"; Folder = $big }
)

# Aquecimento (descartado): 1ª abertura do ControlFS extrai o .exe único; a 1ª do Files monta o pacote; a do Explorador
# cria o cache de miniaturas/ícones da pasta.
foreach ($app in $apps.Keys) {
    $w = Measure-One $app "warmup" $small "warmup"
    Write-Host "Aquecimento $app : $(if ($w.Error) { $w.Error } else { "janela em $($w.WindowMs) ms" })"
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($s in $scenarios) {
    for ($i = 1; $i -le $Runs; $i++) {
        foreach ($app in $apps.Keys) {
            $r = Measure-One $app $s.Id $s.Folder "run$i"
            $rows.Add($r)
            Write-Host ("[{0}/{1}/run{2}] {3}" -f $app, $s.Id, $i, ($r | ConvertTo-Json -Compress))
        }
    }
}

# ---- resumo ------------------------------------------------------------------------------------------------------------
$summary = foreach ($s in $scenarios) {
    foreach ($app in @("ControlFS", "Explorer", "Files")) {
        if (-not $apps.Contains($app)) { continue }
        $ok = @($rows | Where-Object { $_.App -eq $app -and $_.Scenario -eq $s.Id -and -not $_.Error })
        [pscustomobject]@{
            App = $app; Scenario = $s.Id; Label = $s.Label; Runs = $ok.Count
            WindowMs = Get-Median ($ok.WindowMs); FirstFrameMs = Get-Median ($ok.FirstFrameMs)
            WsMB = Get-Median ($ok.WsMB); PrivateMB = Get-Median ($ok.PrivateMB); CpuPct = Get-Median ($ok.CpuPct)
            Threads = Get-Median ($ok.Threads); Procs = Get-Median ($ok.Procs)
            AfterCloseWsMB = Get-Median ($ok.AfterCloseWsMB); AfterClosePrivateMB = Get-Median ($ok.AfterClosePrivateMB)
            AbsWsMB = Get-Median ($ok.AbsWsMB); AbsPrivateMB = Get-Median ($ok.AbsPrivateMB); AbsCpuPct = Get-Median ($ok.AbsCpuPct)
        }
    }
}

function Fmt($v) { if ($null -eq $v) { "n/a" } else { [string]$v } }
$os = [Environment]::OSVersion.Version.ToString()
$sha = if ($env:GITHUB_SHA) { $env:GITHUB_SHA.Substring(0, 7) } else { "local" }
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("### ControlFS vs File Explorer vs Files (commit $sha, Windows $os, $([Environment]::ProcessorCount) logical CPUs, no GPU/WARP, $(Get-Date -Format 'yyyy-MM-dd'))")
[void]$md.AppendLine("")
[void]$md.AppendLine("Median of $Runs runs after a discarded warm-up; $IdleSeconds s measured after $SettleSeconds s settle. Sum of ALL processes of each app. CPU % = share of ONE core. Explorer = delta of the shell (explorer.exe) with the window open vs. without it; ControlFS and Files = absolute totals.")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Scenario | App | Runs | Procs | Window (ms) | Working set (MB) | Private bytes (MB) | CPU (% of 1 core) | Threads |")
[void]$md.AppendLine("|---|---|---|---|---|---|---|---|---|")
foreach ($r in $summary) {
    [void]$md.AppendLine("| $($r.Scenario): $($r.Label) | $($r.App) | $($r.Runs) | $(Fmt $r.Procs) | $(Fmt $r.WindowMs) | $(Fmt $r.WsMB) | $(Fmt $r.PrivateMB) | $(Fmt $r.CpuPct) | $(Fmt $r.Threads) |")
}
[void]$md.AppendLine("")
foreach ($r in ($summary | Where-Object { $_.App -eq "Explorer" })) {
    [void]$md.AppendLine("Explorer $($r.Scenario): absolute shell with the window open = $(Fmt $r.AbsWsMB) MB working set, $(Fmt $r.AbsPrivateMB) MB private, $(Fmt $r.AbsCpuPct)% CPU; delta after closing the window = $(Fmt $r.AfterCloseWsMB) MB working set, $(Fmt $r.AfterClosePrivateMB) MB private.")
}
if (-not $apps.Contains("Files")) { [void]$md.AppendLine("Files: **não medido** ($filesNote).") }
[void]$md.AppendLine("Scenario c (navigate into a subfolder and back): not measured (not scriptable the same way in all three apps).")
$text = $md.ToString()
Set-Content -LiteralPath (Join-Path $OutDir "performance-comparison.md") -Value $text -Encoding utf8
@{ commit = $sha; os = $os; files = if ($filesPkg) { $filesPkg.PackageFullName } else { $filesNote }; runs = $rows; summary = $summary } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir "performance-comparison.json") -Encoding utf8
Write-Host $text
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $text }
Remove-Item Env:\DOTNET_BUNDLE_EXTRACT_BASE_DIR -ErrorAction SilentlyContinue
$cfsFail = @($rows | Where-Object { $_.App -eq "ControlFS" -and $_.Error })
if ($cfsFail.Count -gt 0) { Write-Host "::error::ControlFS sem medição em $($cfsFail.Count) execução(ões)"; exit 1 }
