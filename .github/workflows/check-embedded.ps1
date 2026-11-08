# The injected scripts are C# verbatim strings. A single double quote inside one - in the
# JavaScript, in a CSS font stack, or in a comment explaining either - ends the string early and
# the rest of the file stops being code, which arrives as a wall of syntax errors pointing at
# lines that are perfectly valid JavaScript. This checks the sources for that mistake directly,
# so the reason is stated instead of deduced.
$ErrorActionPreference = "Stop"
$bad = 0
Get-ChildItem src -Filter *.cs | ForEach-Object {
    $file = $_
    $text = Get-Content $file.FullName -Raw
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # A verbatim string opens with @" and, in these files, always closes on a line of its own.
        if ($lines[$i] -notmatch '@"\s*$') { continue }
        $open = $i
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            if ($lines[$j] -match '^\s*";\s*$') { break }
            $line = $lines[$j]
            for ($k = 0; $k -lt $line.Length; $k++) {
                if ($line[$k] -ne '"') { continue }
                $after = ($k + 1 -lt $line.Length) -and ($line[$k + 1] -eq '"')
                $before = ($k -gt 0) -and ($line[$k - 1] -eq '"')
                if (-not ($after -or $before)) {
                    Write-Host "$($file.Name):$($j + 1) has a lone double quote inside the verbatim string opened at line $($open + 1)"
                    Write-Host "    $($line.Trim())"
                    $bad++
                    $k++
                }
            }
        }
        $i = $j
    }
}
if ($bad -gt 0) { throw "$bad lone double quote(s) inside embedded strings" }
Write-Host "Embedded scripts contain no lone double quotes."
