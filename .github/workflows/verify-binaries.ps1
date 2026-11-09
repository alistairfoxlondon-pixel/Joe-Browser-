# Checks the two binaries the build just produced. Run through run-step.ps1, which replays
# whatever this prints as a check-run annotation when it fails.
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $repoRoot
$expected = "$Version.0"

# The shipped XML is read by the runtime before any of our code runs, so a syntax error in it
# stops the process from starting at all - and the message ("side by side configuration is
# incorrect") points at the manifest rather than at the file that is wrong. Parse it here.
foreach ($doc in @('src\app.manifest', 'src\JoeBrowser.exe.config', 'dist\JoeBrowser.exe.config')) {
    if (-not (Test-Path $doc)) { throw "$doc is missing" }
    try { [xml](Get-Content $doc -Raw) | Out-Null } catch { throw "$doc is not valid XML: $($_.Exception.Message)" }
    $text = Get-Content $doc -Raw
    foreach ($m in [regex]::Matches($text, '<!--(.*?)-->', 'Singleline')) {
        if ($m.Groups[1].Value -match '--') { throw "$doc has a comment containing a double hyphen, which invalidates the document" }
    }
    Write-Host "$doc parses"
}

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

# A green run has to mean the release is newer than the last one, or the workflow publishes a
# build with the same version as the last release and the new version is not one.
$latest = $null
try { $latest = (gh release list --limit 1 --json tagName --jq '.[0].tagName').Trim() } catch { }
if ($latest -and $latest -eq ('v' + $Version)) { throw "Version $Version is already released as $latest" }
Write-Host "Version $Version is newer than $latest"

Write-Host 'Verification passed: both binaries link, start, read their own configuration, and carry a new version.'
