# Diagnostic, temporary: works out why the freshly built JoeBrowser.exe cannot start with
# "side-by-side configuration is incorrect". Compares a build made with the embedded manifest
# against one made without it, and reports the outcome through check-run annotations.
$ErrorActionPreference = "Continue"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $repoRoot
$log = New-Object System.Collections.Generic.List[string]
$results = New-Object System.Collections.Generic.List[string]

function Say([string]$s) { $log.Add($s) }

function Try-Run([string]$exe) {
    try {
        $p = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru `
             -RedirectStandardOutput "$env:TEMP\o.txt" -RedirectStandardError "$env:TEMP\e.txt"
        if (-not $p.WaitForExit(30000)) { try { $p.Kill() } catch { }; return "TIMEOUT" }
        return "exit=$($p.ExitCode)"
    } catch {
        $m = $_.Exception.Message -replace "\s+", " "
        if ($m.Length -gt 160) { $m = $m.Substring(0, 160) }
        return "LAUNCH-FAIL: $m"
    }
}

# 1. A trivial WinForms-shaped program, compiled the way the build compiles the app.
$csc = ""
$info = (dotnet --info 2>&1 | Out-String)
if ($info -match 'Base Path:\s*(.+)') { $csc = Join-Path $Matches[1].Trim() "Roslyn\bincore\csc.dll" }
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll") |
    ForEach-Object { Join-Path $fw $_ } | Where-Object { Test-Path $_ } | ForEach-Object { "/r:`"$_`"" }

$prog = Join-Path $env:TEMP "t.cs"
'class T { static int Main() { System.Console.WriteLine("ok"); return 0; } }' | Set-Content $prog -Encoding ASCII

function Compile-Test([string]$name, [string[]]$extra) {
    $out = Join-Path $env:TEMP "$name.exe"
    $a = @($csc, "/nologo", "/target:exe", "/langversion:7.3", "/out:`"$out`"") + $refs + $extra + @("`"$prog`"")
    $text = (& dotnet @a 2>&1 | Out-String)
    if ($text -match "error") { $results.Add("compile[$name] FAILED: $(($text -replace '\s+', ' ').Substring(0, 200))"); return }
    $results.Add("run[$name] " + (Try-Run $out))
}

Compile-Test "t_nomanifest" @()
Compile-Test "t_manifest" @("/win32manifest:`"$repoRoot\src\app.manifest`"")

# 2. The real binary, with and without the manifest.
$app = Join-Path $repoRoot "dist\JoeBrowser.exe"
if (Test-Path $app) {
    $results.Add("run[real]" + (Try-Run $app))
} else {
    $results.Add("run[real] dist\JoeBrowser.exe missing")
}

# 3. What the framework says about the activation.
foreach ($evt in @("Application", "System")) {
    try {
        $entries = Get-WinEvent -LogName $evt -MaxEvents 40 -ErrorAction Stop |
            Where-Object { $_.ProviderName -match "SideBySide" } | Select-Object -First 3
        foreach ($e in $entries) {
            $results.Add("event[$evt] " + (($e.Message -replace "\s+", " ").Substring(0, [Math]::Min(300, ($e.Message -replace "\s+", " ").Length))))
        }
    } catch { $results.Add("event[$evt] none") }
}

# 4. The installer for comparison, and sxstrace's own verdict if it is on the image.
$setup = Join-Path $repoRoot "dist\JoeBrowserSetup.exe"
if (Test-Path $setup) { $results.Add("run[setup] " + (Try-Run $setup)) }

foreach ($r in $results) { Write-Host "RESULT $r" }
$chunk = ""
foreach ($line in $results) {
    if ($chunk.Length + $line.Length + 3 -gt 850) { Write-Host "::notice title=manifest::$chunk"; $chunk = "" }
    $chunk = if ($chunk) { $chunk + " || " + $line } else { $line }
}
if ($chunk) { Write-Host "::notice title=manifest::$chunk" }
