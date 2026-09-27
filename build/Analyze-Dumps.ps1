<#
.SYNOPSIS
  Analisa com o cdb (Debugging Tools do Windows SDK, presente no runner) os dumps que o WER gravou quando o ControlFS
  caiu, e imprime a exceção guardada (stowed) do WinUI, o código e a pilha. Sem dumps ou sem cdb, não faz nada.
#>
param(
    [Parameter(Mandatory = $true)] [string] $DumpDir,
    [Parameter(Mandatory = $true)] [string] $OutFile
)
$ErrorActionPreference = "Continue"
$dumps = Get-ChildItem $DumpDir -Filter *.dmp -Recurse -ErrorAction SilentlyContinue
if (-not $dumps) { return }
$cdb = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64\cdb.exe", "$env:ProgramFiles\Windows Kits\10\Debuggers\x64\cdb.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $cdb) { Write-Host "cdb não encontrado; dumps em $DumpDir"; return }
foreach ($dump in $dumps) {
    Write-Host "---- análise do dump $($dump.Name) ----"
    & $cdb.FullName -z $dump.FullName -c ".symfix; .reload /f Microsoft.UI.Xaml.dll; !analyze -v; .exr -1; .ecxr; kc 50; q" 2>&1 |
        Tee-Object -FilePath $OutFile -Append |
        Select-String -Pattern "STOWED|Stowed|stowed|ERROR_CODE|EXCEPTION_CODE_STR|EXCEPTION_MESSAGE|ExceptionCode|FAULTING_SOURCE|SYMBOL_NAME|FAILURE_BUCKET|Layout|layout|ControlFS" -Context 0,2 |
        Select-Object -First 80 | ForEach-Object { Write-Host $_.Line; $_.Context.PostContext | ForEach-Object { Write-Host "    $_" } }
    Write-Host "---- pilha ----"
    Get-Content $OutFile | Select-String -Pattern "^\s*[0-9a-f]{2}\s" | Select-Object -First 50 | ForEach-Object { Write-Host $_.Line }
}
