<#
.SYNOPSIS
  Abre o ControlFS.exe, espera, e coleta evidências: processo vivo, janela, código de saída,
  erros do log de eventos do Windows, logs do próprio app e um print da tela. Usado pela CI.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [int] $Seconds = 25
)
$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$started = Get-Date
$p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
Start-Sleep -Seconds $Seconds
$p.Refresh()

$alive = -not $p.HasExited
$report = [ordered]@{
    exe = $Exe
    alive = $alive
    exitCode = if ($p.HasExited) { '0x{0:X8}' -f $p.ExitCode } else { $null }
    mainWindowTitle = if ($alive) { $p.MainWindowTitle } else { $null }
    mainWindowHandle = if ($alive) { [int64]$p.MainWindowHandle } else { 0 }
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
try {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save((Join-Path $OutDir "screen.png"))
    $g.Dispose(); $bmp.Dispose()
} catch { $report.screenshotError = $_.Exception.Message }

$events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started.AddSeconds(-5) } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting', 'SideBySide', 'Application Hang' }
$events | Format-List TimeCreated, ProviderName, Id, Message | Out-String -Width 400 | Set-Content (Join-Path $OutDir "events.txt")

$appData = Join-Path $env:LOCALAPPDATA "ControlFS"
if (Test-Path $appData) {
    Get-ChildItem $appData -Recurse | Select-Object FullName, Length | Out-String -Width 400 | Set-Content (Join-Path $OutDir "appdata.txt")
    Get-ChildItem (Join-Path $appData "logs") -Filter *.log -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $OutDir }
}
if ($alive) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }

Get-ChildItem (Split-Path -Parent $Exe) -File | Where-Object { $_.Extension -in '.pri', '.json' -or $_.Name -like 'Microsoft.UI.Xaml*' -or $_.Name -like 'Microsoft.WindowsAppRuntime*' } |
    Select-Object Name, Length | Out-String -Width 200 | Set-Content (Join-Path $OutDir "package-files.txt")
Write-Host "---- arquivos relevantes do pacote ----"; Get-Content (Join-Path $OutDir "package-files.txt")
$report | ConvertTo-Json | Set-Content (Join-Path $OutDir "report.json")
Write-Host ($report | ConvertTo-Json)
Write-Host "---- eventos ----"
Get-Content (Join-Path $OutDir "events.txt") | Select-Object -First 80
Get-ChildItem $OutDir -Filter *.log | ForEach-Object { Write-Host "---- $($_.Name) ----"; Get-Content $_.FullName | Select-Object -First 120 }
if (-not $alive -or $report.mainWindowHandle -eq 0) { Write-Host "::error::ControlFS não ficou aberto com janela"; exit 1 }
Write-Host "ControlFS abriu e manteve a janela por $Seconds s"
