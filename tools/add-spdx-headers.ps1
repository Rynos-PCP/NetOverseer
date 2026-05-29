$header = "// SPDX-License-Identifier: Apache-2.0`r`n// Copyright (c) 2026 Rynos-PCP`r`n`r`n"
$root = 'C:\Projects\NetOverseer'
$files = Get-ChildItem -Path "$root\src","$root\tests" -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$added = 0; $skipped = 0
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
foreach ($f in $files) {
    $firstLine = Get-Content $f.FullName -TotalCount 1 -ErrorAction SilentlyContinue
    if ($firstLine -match 'SPDX-License-Identifier') { $skipped++; continue }
    $content = [System.IO.File]::ReadAllText($f.FullName)
    [System.IO.File]::WriteAllText($f.FullName, $header + $content, $utf8NoBom)
    $added++
}
"Added: $added, Skipped: $skipped, Total: $($files.Count)"
