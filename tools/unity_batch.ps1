# NoWinGuard-safe headless wrapper for Unity 6000.4.7f1 (CREATE_NO_WINDOW / CreateNoWindow = $true).
# Compatible with both Windows PowerShell 5.1 and PowerShell 7+.
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity_batch.ps1 -Log bootstrap -Method Crusher.Editor.ProjectBootstrap.InitAll
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity_batch.ps1 -Log gfx_gate2 -Method Crusher.Editor.OffscreenGate.Run -KeepGraphics

param(
    [Parameter(Mandatory = $true)][string]$Log,
    [string]$Method = "",
    [string]$BuildTarget = "Android",
    [switch]$KeepGraphics,
    [int]$TimeoutSec = 1800,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$ExtraArgs
)

$ErrorActionPreference = 'Stop'
$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe'
if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity 6000.4.7f1 not found at $UnityExe"
    exit 2
}

function Quote-WinArg([string]$s) {
    if ($null -eq $s) { return '""' }
    if ($s.Length -eq 0) { return '""' }
    if ($s -notmatch '[\s"]') { return $s }
    $escaped = $s -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$LogsDir = Join-Path $ProjectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
$LogPath = Join-Path $LogsDir "$Log.log"
if (Test-Path $LogPath) { Remove-Item -Force $LogPath }

$ArgList = New-Object System.Collections.Generic.List[string]
$ArgList.Add('-batchmode')
if (-not $KeepGraphics -and -not $Log.StartsWith('gfx')) {
    $ArgList.Add('-nographics')
}
$ArgList.Add('-quit')
$ArgList.Add('-licensingIpc')
$ArgList.Add('LicenseClient-USER-crusher')
$ArgList.Add('-adb2')
$ArgList.Add('-suppresscias')
$ArgList.Add('-projectPath')
$ArgList.Add($ProjectRoot)
if ($BuildTarget) {
    $ArgList.Add('-buildTarget')
    $ArgList.Add($BuildTarget)
}
$ArgList.Add('-logFile')
$ArgList.Add($LogPath)
if ($Method) {
    $ArgList.Add('-executeMethod')
    $ArgList.Add($Method)
}
if ($ExtraArgs) {
    foreach ($a in $ExtraArgs) { $ArgList.Add($a) }
}

$QuotedArgs = ($ArgList | ForEach-Object { Quote-WinArg $_ }) -join ' '

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $UnityExe
$psi.Arguments = $QuotedArgs
$psi.WorkingDirectory = $ProjectRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()
$stdOut = $proc.StandardOutput.ReadToEndAsync()
$stdErr = $proc.StandardError.ReadToEndAsync()

if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    try { $proc.Kill() } catch {}
    Write-Output "UNITY_TIMEOUT after ${TimeoutSec}s (Log: $LogPath)"
    exit 124
}
[void]$stdOut.Result
[void]$stdErr.Result
$exitCode = $proc.ExitCode
Write-Output "UNITY_EXIT_CODE=$exitCode | LOG=$LogPath"

if (Test-Path $LogPath) {
    $matches = Select-String -Path $LogPath -Pattern '\[ProjectBootstrap\]|\[Crusher\]|error CS|CompilerError|Shader error|Exception:|Build succeeded|Build failed|Exiting batchmode' -ErrorAction SilentlyContinue
    $matches | Select-Object -Last 60 | ForEach-Object { Write-Output $_.Line }
}

exit $exitCode
