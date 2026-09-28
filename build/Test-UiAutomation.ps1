<#
.SYNOPSIS
  Testes de UI Automation (UIA) no app real (#83): abre o ControlFS.exe, manda teclas para a janela e confere pelo
  UIA o que o WinUI desenhou: título do modal, opção/tecla focada (anel de foco), escopo do modal e teclado virtual.
  Usado pelo smoke.yml (Windows PowerShell 5.1: UIAutomationClient vem do .NET Framework).

  O foco do ControlFS é lógico (anel desenhado, não foco do XAML); o ModalView marca o texto focado com AutomationIds
  estáveis (ControlFS.ModalTitle, ControlFS.FocusedOption, ControlFS.FocusedKey, ControlFS.KeyboardField).
  Ver docs/TESTING.md → "Testes de UI Automation (#83)".
#>
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [int] $TimeoutSeconds = 15
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UiaWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$results = New-Object System.Collections.Generic.List[object]
$script:step = 0

function Save-Screen([string]$name) {
    try {
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $bmp.Save((Join-Path $OutDir "$name.png"))
        $g.Dispose(); $bmp.Dispose()
    } catch { Write-Host "Print falhou: $($_.Exception.Message)" }
}

function Find-Id([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-Name([string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Name-Of([string]$id) {
    $e = Find-Id $id
    if ($null -eq $e) { return $null }
    try { return $e.Current.Name } catch { return $null } # o modal é refeito a cada quadro: o elemento pode sumir
}

function Send([string]$keys) {
    [void][UiaWin32]::SetForegroundWindow($script:hwnd)
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 250
}

# Espera a condição valer (o render do WinUI é assíncrono); registra o passo e para no primeiro erro.
function Check([string]$description, [scriptblock]$condition) {
    $script:step++
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ok = $false
    do {
        try { $ok = [bool](& $condition) } catch { $ok = $false }
        if (-not $ok) { Start-Sleep -Milliseconds 200 }
    } while (-not $ok -and (Get-Date) -lt $deadline)
    $observed = "título=`"$(Name-Of 'ControlFS.ModalTitle')`" opção=`"$(Name-Of 'ControlFS.FocusedOption')`" tecla=`"$(Name-Of 'ControlFS.FocusedKey')`""
    $results.Add([ordered]@{ step = $script:step; check = $description; ok = $ok; observed = $observed })
    Write-Host ("[{0}] {1}: {2} ({3})" -f $script:step, ($(if ($ok) { "OK" } else { "FALHOU" })), $description, $observed)
    if (-not $ok) {
        Save-Screen ("falha-{0:D2}" -f $script:step)
        throw "Falhou: $description"
    }
}

# Dados limpos e sem verificar atualizações: um aviso de nova versão abriria um modal antes dos testes.
$data = Join-Path (Split-Path -Parent $Exe) "ControlFS_Data"
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "AutoCheckUpdates": false, "OnboardingCompleted": true }')
$p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
$failed = $null
try {
    $deadline = (Get-Date).AddSeconds(60)
    do { Start-Sleep -Milliseconds 500; $p.Refresh() } while (-not $p.HasExited -and $p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw "ControlFS não abriu uma janela" }
    $script:hwnd = $p.MainWindowHandle
    [void][UiaWin32]::ShowWindow($script:hwnd, 3) # maximizada: nada fora da tela
    $script:root = $AE::FromHandle($script:hwnd)
    Start-Sleep -Seconds 3 # primeira renderização e ícones

    # 1. Menu: abre com um item focado, e o anel acompanha a navegação.
    Check "sem modal ao abrir" { $null -eq (Find-Id 'ControlFS.ModalTitle') }
    Send "{F10}"
    Check "F10 abre o Menu" { (Name-Of 'ControlFS.ModalTitle') -eq 'Menu' }
    $first = Name-Of 'ControlFS.FocusedOption'
    Check "Menu abre com uma opção focada" { -not [string]::IsNullOrEmpty($first) }
    Send "{DOWN}"
    Check "↓ move o foco para outra opção" { $n = Name-Of 'ControlFS.FocusedOption'; $n -and $n -ne $first }
    Send "{PGDN}"
    Check "PageDown leva o foco à última opção (Sair)" { (Name-Of 'ControlFS.FocusedOption') -eq 'Sair' }

    # 2. Diálogo sensível: o foco cai na opção segura, e o modal prende a entrada.
    Send "{ENTER}"
    Check "Sair abre a confirmação" { (Name-Of 'ControlFS.ModalTitle') -eq 'Sair do ControlFS?' }
    Check "foco na opção segura (Cancelar)" { (Name-Of 'ControlFS.FocusedOption') -eq 'Cancelar' }
    Send "{F10}"
    Check "F10 dentro da confirmação não abre o Menu por cima" { (Name-Of 'ControlFS.ModalTitle') -eq 'Sair do ControlFS?' -and (Name-Of 'ControlFS.FocusedOption') -eq 'Cancelar' }
    Send "{RIGHT}"
    Check "→ move o foco para Sair (perigosa)" { (Name-Of 'ControlFS.FocusedOption') -eq 'Sair' -and (Name-Of 'ControlFS.ModalTitle') -eq 'Sair do ControlFS?' }
    Send "{LEFT}"
    Check "← volta para Cancelar" { (Name-Of 'ControlFS.FocusedOption') -eq 'Cancelar' }
    Send "{ESC}"
    Check "Esc fecha a confirmação sem sair" { $null -eq (Find-Id 'ControlFS.ModalTitle') -and -not $p.HasExited }

    # 3. Teclado virtual: abre na busca de uma pasta, desenha as teclas, recebe texto e move o foco.
    Send "{ENTER}"
    Start-Sleep -Seconds 2 # carrega a primeira pasta do início
    Send "^f"
    Check "Ctrl+F abre o teclado virtual da busca" { (Name-Of 'ControlFS.ModalTitle') -like 'Buscar*' }
    Check "teclas desenhadas (q, espaço, Concluir) e uma focada" {
        $null -ne (Find-Name 'q') -and $null -ne (Find-Name 'espaço') -and $null -ne (Find-Name 'Concluir') -and $null -ne (Find-Id 'ControlFS.FocusedKey')
    }
    $key = Name-Of 'ControlFS.FocusedKey'
    Send "{DOWN}"
    Check "↓ move o foco do teclado" { $n = Name-Of 'ControlFS.FocusedKey'; $n -and $n -ne $key }
    Send "abc"
    Check "texto digitado aparece no campo" { (Name-Of 'ControlFS.KeyboardField') -like 'Texto: abc,*' }
    Send "{ESC}"
    Check "Esc fecha o teclado" { $null -eq (Find-Id 'ControlFS.ModalTitle') }
    Save-Screen "final"
} catch {
    $failed = $_.Exception.Message
    Write-Host "::error::UI Automation: $failed"
} finally {
    $p.Refresh()
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Get-ChildItem (Join-Path (Split-Path -Parent $Exe) "ControlFS_Data\logs") -Filter *.log -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $OutDir -Force }
    $results | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutDir "uia-results.json") -Encoding UTF8
}
if ($failed) { exit 1 }
Write-Host "UI Automation: $($results.Count) verificações OK"
exit 0
