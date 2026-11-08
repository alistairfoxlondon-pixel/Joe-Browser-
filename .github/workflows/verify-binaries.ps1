# Checks the two binaries the build just produced. Run through run-step.ps1, which replays
# whatever this prints as a check-run annotation when it fails.
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $repoRoot
$expected = "$Version.0"

foreach ($f in @('dist\JoeBrowser.exe', 'dist\JoeBrowserSetup.exe')) {
    if (-not (Test-Path $f)) { throw "$f was not produced" }
    $size = (Get-Item $f).Length
    if ($size -lt 200KB) { throw "$f is only $size bytes - the build did not link properly" }
    $fileVersion = (Get-Item $f).VersionInfo.FileVersion
    if ($fileVersion -ne $expected) { throw "$f reports $fileVersion, expected $expected" }
    Write-Host ("{0}  {1:N0} bytes  file version {2}" -f $f, $size, $fileVersion)
}

# A binary that links is not a binary that runs. --selftest starts the freshly built EXE, has
# it read the state that matters, and exits with a code - no window, so it works on a runner
# with no interactive desktop. --help opens a MessageBox there and blocks until the timeout.
foreach ($f in @('dist\JoeBrowser.exe', 'dist\JoeBrowserSetup.exe')) {
    $out = Join-Path $env:RUNNER_TEMP 'selftest.out'
    $err = Join-Path $env:RUNNER_TEMP 'selftest.err'
    $p = Start-Process -FilePath (Join-Path $repoRoot $f) -ArgumentList '--selftest' -PassThru `
         -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $p.WaitForExit(60000)) {
        try { $p.Kill() } catch { }
        throw "$f --selftest did not exit within 60 seconds"
    }
    $said = @(Get-Content $out, $err -ErrorAction SilentlyContinue | Where-Object { $_.Trim() }) -join ' / '
    Write-Host "$f --selftest exit=$($p.ExitCode) said=$said"
    if ($p.ExitCode -ne 0) { throw "$f --selftest exited with $($p.ExitCode): $said" }
}

Write-Host 'Verification passed: both binaries link, start and read their own configuration.'
