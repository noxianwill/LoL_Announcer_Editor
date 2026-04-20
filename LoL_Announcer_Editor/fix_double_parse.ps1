$ErrorActionPreference = "Stop"
$file = "LoL_Announcer_Editor\MainWindow.xaml.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

Write-Host "File length: $($content.Length)"

# Check if double-parse bug exists
$marker = "ParseEventBnkFile(bnkPath, sections)"
if ($content.IndexOf($marker) -ge 0) {
    Write-Host "Found double-parse bug at index $($content.IndexOf($marker))"
} else {
    Write-Host "Marker not found - file may already be fixed"
    exit 0
}

# Build OLD block using StringBuilder to avoid quoting issues
$oldLines = [System.Collections.Generic.List[string]]::new()
$oldLines.Add("                // Parse BNK using BNKSections and ParseEventBnkFile")
$oldLines.Add("                WemAdditionLogger.Log(`"Parsing BNK file (Quick Parse)...`");")
$oldLines.Add("                var sections = new BNKSections();")
$oldLines.Add("                if (!BnkExtractor.ParseEventBnkFile(bnkPath, sections))")
$oldLines.Add("                {")
$oldLines.Add("                    WemAdditionLogger.LogError(`"Failed to parse BNK file in quick mode`");")
$oldLines.Add("                    System.Windows.MessageBox.Show(`"Failed to parse BNK file. Please check if the file is valid.`", `"Parsing Error`", MessageBoxButton.OK, MessageBoxImage.Error);")
$oldLines.Add("                    return;")
$oldLines.Add("                }")
$oldLines.Add("                WemAdditionLogger.Log(`$`"BNK parsed: {sections.Sounds.Count} sounds, {sections.Events.Count} events`");")
$oldLines.Add("")
$oldLines.Add("                // Store BNK structure in memory for WEM addition feature")
$oldLines.Add("                _currentBnkSections = sections;")
$oldLines.Add("                _currentBnkPath = bnkPath;")
$oldLines.Add("                _currentWpkPath = wpkPath;")
$oldLines.Add("")
$oldLines.Add("                // Initialize full-fidelity BNK structure")
$oldLines.Add("                WemAdditionLogger.Log(`"Parsing BNK file (Full Fidelity Mode)...`");")
$oldLines.Add("                _fullFidelityBnk = new BnkFullFidelity();")
$oldLines.Add("                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);")
$oldLines.Add("                if (!fullFidelityOk)")
$oldLines.Add("                {")
$oldLines.Add("                    WemAdditionLogger.LogError(`"Failed to parse BNK file in Full Fidelity mode`");")
$oldLines.Add("                }")
$oldLines.Add("                else")
$oldLines.Add("                {")
$oldLines.Add("                    WemAdditionLogger.Log(`$`"Full Fidelity BNK parsed: {_fullFidelityBnk.AllObjectsInOrder.Count} objects found`");")
$oldLines.Add("                    // Log original BNK structure for debugging")
$oldLines.Add("                    LogBnkStructureToDebug(_fullFidelityBnk, `"ORIGINAL`");")
$oldLines.Add("                }")
$old = [string]::Join("`r`n", $oldLines)

$newLines = [System.Collections.Generic.List[string]]::new()
$newLines.Add("                // BNK - single pass via Full-Fidelity only.")
$newLines.Add("                // BUG FIXED: previously ParseEventBnkFile was called FIRST, then")
$newLines.Add("                // ParseEventBnkFile_FullFidelity on the SAME BNKSections object,")
$newLines.Add("                // which doubled every list (Sounds, Events, EventActions, RandomContainers)")
$newLines.Add("                // and corrupted all ID lookups, causing WEMs to never resolve to events.")
$newLines.Add("                WemAdditionLogger.Log(`"Parsing BNK file (Full Fidelity)...`");")
$newLines.Add("                var sections = new BNKSections();")
$newLines.Add("                _fullFidelityBnk = new BnkFullFidelity();")
$newLines.Add("                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);")
$newLines.Add("                if (!fullFidelityOk)")
$newLines.Add("                {")
$newLines.Add("                    WemAdditionLogger.LogError(`"Failed to parse BNK file`");")
$newLines.Add("                    System.Windows.MessageBox.Show(`"Failed to parse BNK file. Please check if the file is valid.`", `"Parsing Error`", MessageBoxButton.OK, MessageBoxImage.Error);")
$newLines.Add("                    return;")
$newLines.Add("                }")
$newLines.Add("                WemAdditionLogger.Log(`$`"BNK parsed: {sections.Sounds.Count} sounds, {sections.Events.Count} events, {sections.RandomContainers.Count} containers`");")
$newLines.Add("                LogBnkStructureToDebug(_fullFidelityBnk, `"ORIGINAL`");")
$newLines.Add("")
$newLines.Add("                // Store BNK structure in memory for WEM addition feature")
$newLines.Add("                _currentBnkSections = sections;")
$newLines.Add("                _currentBnkPath = bnkPath;")
$newLines.Add("                _currentWpkPath = wpkPath;")
$new = [string]::Join("`r`n", $newLines)

if ($content.Contains($old)) {
    Write-Host "OLD block matched exactly. Replacing..."
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
    Write-Host "SUCCESS: double-parse bug fixed."
} else {
    Write-Host "Exact match failed. Trying to find partial overlap..."
    # Find the index of "ParseEventBnkFile(bnkPath" to show context
    $idx = $content.IndexOf("ParseEventBnkFile(bnkPath, sections)")
    if ($idx -ge 0) {
        $ctx = $content.Substring([Math]::Max(0, $idx-200), 500)
        Write-Host "Context around bug:"
        Write-Host $ctx
    }
}
