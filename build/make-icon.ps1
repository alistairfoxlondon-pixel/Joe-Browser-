# Generates src\app.ico from the application's own drawing code.
#
# The artwork lives in src\Logo.cs and nowhere else: this script builds the audit harness
# (which compiles that file) and asks it to write the icon. Re-implementing the mark here in
# PowerShell - which is what this script used to do - meant every tweak had to be made twice
# and the two copies silently drifted apart.
param([string]$Out = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path $root "src\app.ico" }

& (Join-Path $PSScriptRoot "audit.ps1") | Out-Null
$audit = Join-Path $root "tools\audit\audit.exe"
if (-not (Test-Path $audit)) { throw "Audit harness missing at $audit" }

& $audit --make-icon $Out
if ($LASTEXITCODE -ne 0) { throw "Icon build failed with exit code $LASTEXITCODE" }
