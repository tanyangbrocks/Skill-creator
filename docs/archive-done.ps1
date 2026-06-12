# docs/archive-done.ps1
# Trims the "latest completed" table in 'Progress.md', keeping only the newest N rows.
# Older rows are appended to docs/history/phase3.md.
#
# Usage (from any directory):
#   powershell -ExecutionPolicy Bypass -File docs\archive-done.ps1 [-Keep 5]

param([int]$Keep = 5)

$root         = Split-Path $PSScriptRoot -Parent
$progressFile = Join-Path $root ([char]0x5BE6 + [char]0x4F5C + [char]0x9032 + [char]0x5EA6 + ".md")
$historyFile  = Join-Path $root "docs\history\phase3.md"
$enc          = New-Object System.Text.UTF8Encoding($false)   # UTF-8 no BOM

# ── Read progress file ────────────────────────────────────────────
$lines = @(Get-Content $progressFile -Encoding UTF8)

# ── Locate the first markdown table separator (pipes + dashes only) ──
# The "latest completed" table is always the first table in the file.
$sepIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\|[-| ]+\|') {
        $sepIdx = $i
        break
    }
}

if ($sepIdx -lt 0) {
    Write-Host "[archive-done] ERROR: table separator not found in $progressFile"
    exit 1
}

$headerIdx = $sepIdx - 1
$dataStart = $sepIdx + 1
$dataEnd   = $dataStart - 1

for ($i = $dataStart; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith('|')) { $dataEnd = $i }
    else { break }
}

$dataRows = @()
if ($dataEnd -ge $dataStart) {
    $dataRows = @($lines[$dataStart..$dataEnd] | Where-Object { $_.StartsWith('|') })
}

if ($dataRows.Count -le $Keep) {
    Write-Host "[archive-done] Table has $($dataRows.Count) rows (threshold $Keep). Nothing to archive."
    exit 0
}

$toArchive = @($dataRows[0..($dataRows.Count - $Keep - 1)])
$toKeep    = @($dataRows[($dataRows.Count - $Keep)..($dataRows.Count - 1)])

# ── Rebuild progress file (keep header+sep, replace data rows) ────
$before   = if ($dataStart -gt 0)                       { @($lines[0..($dataStart - 1)]) }                 else { @() }
$after    = if (($dataEnd + 1) -le ($lines.Count - 1))  { @($lines[($dataEnd + 1)..($lines.Count - 1)]) }  else { @() }
$newLines = $before + $toKeep + $after

[System.IO.File]::WriteAllLines($progressFile, $newLines, $enc)

# ── Append to history file ────────────────────────────────────────
$histDir = Split-Path $historyFile -Parent
if (!(Test-Path $histDir)) { New-Item -ItemType Directory -Force -Path $histDir | Out-Null }

if (!(Test-Path $historyFile)) {
    # Reuse the exact header+separator lines from the source file (avoids hardcoding Chinese)
    $init = @(
        "# Phase 3+ History",
        "",
        "Auto-archived from $progressFile by docs/archive-done.ps1.",
        "",
        $lines[$headerIdx],
        $lines[$sepIdx]
    )
    [System.IO.File]::WriteAllLines($historyFile, $init, $enc)
}

# AppendAllLines 2-arg form uses UTF-8 without BOM by default (.NET)
[System.IO.File]::AppendAllLines($historyFile, [string[]]$toArchive)

Write-Host "[archive-done] Archived $($toArchive.Count) rows to $historyFile"
Write-Host "[archive-done] Kept newest $Keep rows in $progressFile"
