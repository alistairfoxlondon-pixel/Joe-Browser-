# Reports what the Windows image actually offers as a C# compiler, as check-run annotations.
#
# The build failed with nothing but "exit code 1" and the raw job log is unreachable from the
# environment that maintains this repository, so this script answers the question the log would
# have answered: is there a compiler that understands C# 7.3, and what does it need on the
# command line to resolve mscorlib? Every finding is a ::notice, which the API can read.
$ErrorActionPreference = "Continue"

function Note([string]$title, [string]$message) {
    $flat = ($message -replace "\r?\n", " | ").Trim()
    if ($flat.Length -gt 900) { $flat = $flat.Substring(0, 900) + "..." }
    if (-not $flat) { $flat = "(empty)" }
    Write-Host "::notice title=$title::$flat"
}

# --- what the SDK is ------------------------------------------------------------------------
$dn = (dotnet --version 2>&1 | Out-String).Trim()
Note "dotnet --version" $dn
Note "installed SDKs" ((dotnet --list-sdks 2>&1 | Out-String).Trim())

$info = (dotnet --info 2>&1 | Out-String)
if ($info -match 'Base Path:\s*(.+)') {
    $basePath = $Matches[1].Trim()
    Note "dotnet base path" $basePath
    $cscDll = Join-Path $basePath "Roslyn\bincore\csc.dll"
    Note "csc.dll" "$cscDll exists=$(Test-Path $cscDll)"

    # A csc.rsp beside the compiler would supply default references; its absence explains
    # "Predefined type 'System.Object' is not defined" (CS0518) on a referenced-less compile.
    $rsp = Join-Path $basePath "Roslyn\bincore\csc.rsp"
    Note "csc.rsp next to csc.dll" "exists=$(Test-Path $rsp) $(if (Test-Path $rsp) { (Get-Content $rsp | Select-Object -First 6) -join ' | ' })"
} else {
    Note "dotnet base path" "NOT FOUND in dotnet --info"
    $cscDll = ""
}

# --- what the machine offers without the SDK ------------------------------------------------
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
Note "Framework64 directory" "$fw exists=$(Test-Path $fw)"
foreach ($name in @("csc.exe", "mscorlib.dll", "System.dll", "System.Windows.Forms.dll")) {
    $p = Join-Path $fw $name
    $exists = Test-Path $p
    $ver = if ($exists) { (Get-Item $p).VersionInfo.FileVersion } else { "-" }
    Note "framework $name" "exists=$exists version=$ver"
}

# A reference-assembly pack, if present, is a cleaner target than the runtime mscorlib.
$packs = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework"
Note "reference assemblies" "$packs exists=$(Test-Path $packs) $(if (Test-Path $packs) { (Get-ChildItem $packs -Directory | Select-Object -ExpandProperty Name) -join ',' })"

# --- can csc.dll compile anything at all? ---------------------------------------------------
if (-not $cscDll -or -not (Test-Path $cscDll)) {
    Note "probe compile" "skipped, no csc.dll"
    exit 0
}

$dir = Join-Path $env:RUNNER_TEMP "probe"
New-Item -ItemType Directory -Force $dir | Out-Null
$src = Join-Path $dir "p.cs"
'class P { static void Main() { System.Console.WriteLine("hi"); } }' | Set-Content -Path $src -Encoding ASCII

function Try-Compile([string]$label, [string[]]$refs) {
    $out = Join-Path $dir ("p_" + ($label -replace '[^a-zA-Z0-9]', '_') + ".exe")
    $args = @("/nologo", "/target:exe", "/out:$out") + ($refs | ForEach-Object { "/r:$_" }) + @($src)
    $text = (& dotnet $cscDll @args 2>&1 | Out-String).Trim()
    $ok = (Test-Path $out)
    $code = $LASTEXITCODE
    Note "compile: $label" "exit=$code produced=$ok $(if ($text) { $text } else { '(no output)' })"
}

Try-Compile "no references" @()
Try-Compile "mscorlib only" @((Join-Path $fw "mscorlib.dll"))
Try-Compile "build.ps1 reference set" @(
    (Join-Path $fw "System.dll"),
    (Join-Path $fw "System.Core.dll"),
    (Join-Path $fw "System.Drawing.dll"),
    (Join-Path $fw "System.Windows.Forms.dll")
)
