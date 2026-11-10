# Installs the freshly built installer the way a user's machine would, then removes it again.
#
# The self-test proves each binary starts. This proves the pair works together: the installer
# carries the application as an embedded payload, writes it to the per-user location, registers
# an uninstall entry, and the installed copy has to start afterwards. None of that is visible
# from the build directory, and each defect it catches is one a user would meet first.
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $repoRoot

$installed = Join-Path $env:LOCALAPPDATA 'Programs\JoeBrowser'
$exe = Join-Path $installed 'JoeBrowser.exe'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\JoeBrowser'
$startLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Joe Browser.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Joe Browser.lnk'

function Run([string]$file, [string]$arguments) {
    $p = Start-Process -FilePath $file -ArgumentList $arguments -PassThru -Wait
    return $p.ExitCode
}

Write-Host "Installing to $installed"
$code = Run (Join-Path $repoRoot 'dist\JoeBrowserSetup.exe') '/silent /nodesktop'
if ($code -ne 0) { throw "the installer exited with $code" }

if (-not (Test-Path $exe)) { throw "the installed application is not at $exe" }
$size = (Get-Item $exe).Length
Write-Host ("Installed: {0} ({1:N0} bytes)" -f $exe, $size)
if ($size -lt 1MB) { throw "the installed copy is only $size bytes" }

# .NET reads JoeBrowser.exe.config from beside the executable; an installed copy without it
# runs with no DPI handling at all, which is invisible to any check that only starts the app.
$installedConfig = $exe + '.config'
if (-not (Test-Path $installedConfig)) { throw "the installer did not write $installedConfig" }
Write-Host ("Configuration installed: {0} ({1:N0} bytes)" -f $installedConfig, (Get-Item $installedConfig).Length)

$installedVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($installedVersion -ne "$Version.0") { throw "the installed copy reports $installedVersion, expected $Version.0" }

# The uninstall entry is what makes the app appear in Apps and Features. Without it there is no
# supported way to remove Joe Browser.
if (-not (Test-Path $uninstallKey)) { throw "no uninstall entry was registered" }
$entry = Get-ItemProperty $uninstallKey
Write-Host ("Add/Remove Programs entry: {0} {1} -> {2}" -f $entry.DisplayName, $entry.DisplayVersion, $entry.UninstallString)
if ($entry.DisplayName -ne 'Joe Browser') { throw "the uninstall entry is named '$($entry.DisplayName)'" }
if ($entry.DisplayVersion -ne $Version) { throw "the uninstall entry reports $($entry.DisplayVersion), expected $Version" }
if (-not (Test-Path $startLink)) { throw "no Start menu shortcut was created" }
Write-Host "Start menu shortcut created"

# The point of installing is that the installed copy runs - including its own configuration
# file, which lives beside it and is copied by the installer, not by the build.
$p = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru -RedirectStandardOutput "$env:TEMP\installed.out" -RedirectStandardError "$env:TEMP\installed.err"
# Touching the handle caches it, and a cached handle is what makes ExitCode readable
# afterwards. Without it the property comes back empty and a passing self-test reads as a
# failure.
$null = $p.Handle
if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch { }; throw 'the installed copy did not exit within 60 seconds' }
$said = @(Get-Content "$env:TEMP\installed.out", "$env:TEMP\installed.err" -ErrorAction SilentlyContinue | Where-Object { $_.Trim() }) -join ' / '
Write-Host "Installed copy --selftest exit=$($p.ExitCode) said=$said"
if ($p.ExitCode -ne 0) { throw "the installed copy failed its self-test: $said" }

# Uninstall, through the same string Apps and Features runs.
Write-Host "Uninstalling"
$code = Run $exe '--uninstall --silent'
if ($code -ne 0) { throw "the uninstaller exited with $code" }

# The executable deletes itself from a detached shell, so give it a moment.
for ($i = 0; $i -lt 20 -and (Test-Path $exe); $i++) { Start-Sleep -Milliseconds 500 }
if (Test-Path $exe) { throw "the uninstaller left $exe behind" }
if (Test-Path $uninstallKey) { throw "the uninstaller left the Apps and Features entry behind" }
if (Test-Path $startLink) { throw "the uninstaller left the Start menu shortcut behind" }
Write-Host "Uninstall removed the executable, the shortcut and the registry entry"

Write-Host 'Install test passed: install, run the installed copy, uninstall.'
