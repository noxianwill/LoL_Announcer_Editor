$f = 'MainWindow.xaml.cs'
$lines = [System.IO.File]::ReadAllLines($f)
Write-Host "Total lines: $($lines.Length)"
Write-Host "Line 312: [$($lines[311])]"
Write-Host "Line 313: [$($lines[312])]"
Write-Host "Line 314: [$($lines[313])]"
Write-Host "Line 315: [$($lines[314])]"
Write-Host "Line 320: [$($lines[319])]"
Write-Host "Line 321: [$($lines[320])]"
Write-Host "Line 322: [$($lines[321])]"

# Lines to remove: 312-321 (0-indexed: 311-320), except keep line 314 (var sections = new BNKSections();)
# We'll replace lines 311-320 with:
#   line 311: (blank - leave as is, it's before our block)
#   line 312-321: replaced with 2 lines (comment + var sections declaration)

# Build result: everything before line 312 + replacement + everything after line 321
$replacement = @(
    "                // BNK sections - populated by ParseEventBnkFile_FullFidelity (single pass, no double-fill).",
    "                var sections = new BNKSections();"
)

$result = [System.Collections.Generic.List[string]]::new()

# Add lines 1..311 (indices 0..310)
for ($i = 0; $i -lt 311; $i++) { $result.Add($lines[$i]) }

# Add replacement
foreach ($r in $replacement) { $result.Add($r) }

# Skip original lines 312..321 (indices 311..320), resume from 322 (index 321)
for ($i = 321; $i -lt $lines.Length; $i++) { $result.Add($lines[$i]) }

Write-Host "Writing $($result.Count) lines back..."
[System.IO.File]::WriteAllLines($f, $result, [System.Text.UTF8Encoding]::new($false))
Write-Host "Done. New line count: $($result.Count)"
Write-Host "Verify around fix:"
Write-Host "  L310: [$($result[309])]"
Write-Host "  L311: [$($result[310])]"
Write-Host "  L312: [$($result[311])]"
Write-Host "  L313: [$($result[312])]"
Write-Host "  L314: [$($result[313])]"
