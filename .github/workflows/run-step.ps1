# Runs a build script in a child PowerShell, captures everything it writes, and on failure
# replays the tail of that output as check-run annotations.
#
# Why: csc writes its diagnostics to stderr, and a step that fails on a compiler error only
# reports "Process completed with exit code 1". Annotations are attached to the check run, so
# a failed build explains itself in the workflow summary instead of only in the raw log.
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string[]]$ScriptArgs = @()
)

$ErrorActionPreference = "Stop"

function Quote-Arg([string]$s) {
    if ($s -match '\s') { return '"' + $s + '"' }
    return $s
}

$out = Join-Path $env:RUNNER_TEMP "step.out.log"
$err = Join-Path $env:RUNNER_TEMP "step.err.log"
$exe = (Get-Process -Id $PID).Path

$resolved = (Resolve-Path $Script).Path
$argList = @("-NoProfile", "-NonInteractive", "-File", (Quote-Arg $resolved))
foreach ($a in $ScriptArgs) { $argList += (Quote-Arg $a) }

$proc = Start-Process -FilePath $exe -ArgumentList ($argList -join " ") -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $out -RedirectStandardError $err

$lines = @()
if (Test-Path $out) { $lines += @(Get-Content $out) }
if (Test-Path $err) { $lines += @(Get-Content $err | ForEach-Object { "stderr: $_" }) }
foreach ($line in $lines) { Write-Host $line }

if ($proc.ExitCode -ne 0) {
    # An annotation holds at most 1024 characters, so pack the tail of the output - where a
    # compiler puts its errors - into a handful of them.
    $chunks = New-Object System.Collections.Generic.List[string]
    $chunk = ""
    foreach ($line in ($lines | Where-Object { $_.Trim() } | Select-Object -Last 60)) {
        if ($chunk.Length + $line.Length + 3 -gt 950) { $chunks.Add($chunk); $chunk = "" }
        $chunk = if ($chunk) { $chunk + " | " + $line.Trim() } else { $line.Trim() }
    }
    if ($chunk) { $chunks.Add($chunk) }
    if ($chunks.Count -eq 0) { $chunks.Add("no output") }

    $total = $chunks.Count
    $i = 0
    foreach ($c in ($chunks | Select-Object -Last 10)) {
        $i++
        Write-Host ("::error title=Build output ({0}/{1})::{2}" -f $i, $total, $c)
    }
    throw "$Script exited with code $($proc.ExitCode)"
}
