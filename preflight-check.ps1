# SkillCreator preflight check — Windows PowerShell 5.1 compatible
# Run: powershell -ExecutionPolicy Bypass -File preflight-check.ps1 [-Verbose]
param([switch]$ShowDetails)

$root = $PSScriptRoot
$src  = "$root\Scripts"
$pass = 0
$fail = 0

function Pass($msg) { Write-Host "  OK  $msg" -ForegroundColor Green;  $script:pass++ }
function Fail($msg) { Write-Host "  NG  $msg" -ForegroundColor Red;    $script:fail++ }
function Warn($msg) { Write-Host "  WW  $msg" -ForegroundColor Yellow }
function Head($msg) { Write-Host "`n-- $msg --" -ForegroundColor Cyan  }

# Extract enum member names from C# source
function Get-EnumValues($file, $enumName) {
    $text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
    $opts = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $pat  = "enum\s+$enumName\s*\{([^}]+)\}"
    $m    = [System.Text.RegularExpressions.Regex]::Match($text, $pat, $opts)
    if (-not $m.Success) { return @() }
    $body = $m.Groups[1].Value
    $body -split "`n" | ForEach-Object {
        ($_ -replace '//.*','').Trim()
    } | Where-Object { $_ -match '^[A-Za-z_]\w*' } | ForEach-Object {
        ($_ -split '[,\s=]')[0]
    } | Where-Object { $_ -ne '' }
}

function Read-UTF8($file) {
    [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
}

# ----------------------------------------------------------------
Head "1. BlockType completeness"

$btFile = "$src\AbilitySystem\VM\BlockType.cs"
$scFile = "$src\UI\ScratchCanvas.cs"
$aeFile = "$src\UI\AbilityEditorUI.cs"
$spFile = "$src\AbilitySystem\VM\SpellCompiler.cs"

$allBTs  = Get-EnumValues $btFile "BlockType"
$scText  = Read-UTF8 $scFile
$aeText  = Read-UTF8 $aeFile
$spText  = Read-UTF8 $spFile

# 1a: ScratchCanvas descriptors
# TotemDone/Hit/Fizzle are used only as If-condition params, not standalone blocks
$scExclude = @('TotemDone','TotemHit','TotemFizzle')
$missSC = $allBTs | Where-Object { ($_ -notin $scExclude) -and ($scText -notmatch "BlockType\.$_\b") }
if ($missSC) { Fail "ScratchCanvas missing descriptors: $($missSC -join ', ')" }
else         { Pass "ScratchCanvas has descriptor for all applicable BlockTypes ($($allBTs.Count - $scExclude.Count))" }

# 1b: AbilityEditorUI palette (TotemDone/Hit/Fizzle intentionally excluded)
$exclude = @('TotemDone','TotemHit','TotemFizzle')
$missAE  = $allBTs | Where-Object {
    ($_ -notin $exclude) -and ($aeText -notmatch "BlockType\.$_\b")
}
if ($missAE) { Fail "AbilityEditorUI palette missing: $($missAE -join ', ')" }
else         { Pass "AbilityEditorUI palette covers all BlockTypes (3 Totem conditions intentionally excluded)" }

# 1c: SpellCompiler (warn only)
$missSP = $allBTs | Where-Object { $spText -notmatch "BlockType\.$_\b" }
if ($missSP) { Warn "SpellCompiler does not reference (may be handled elsewhere): $($missSP -join ', ')" }
else         { Pass "SpellCompiler references all BlockTypes" }

# ----------------------------------------------------------------
Head "2. ItemId / ItemRegistry completeness"

$itemIdFile  = "$src\World\Items\ItemId.cs"
$itemRegFile = "$src\World\Items\ItemRegistry.cs"

$allIds  = Get-EnumValues $itemIdFile "ItemId"
$regText = Read-UTF8 $itemRegFile

$missReg = $allIds | Where-Object { $_ -ne 'None' -and $regText -notmatch "ItemId\.$_\b" }
if ($missReg) { Fail "ItemRegistry missing: $($missReg -join ', ')" }
else          { Pass "ItemRegistry registers all $($allIds.Count) ItemIds" }

# ----------------------------------------------------------------
Head "3. MaterialType / MaterialRegistry completeness"

$matTypeFile = "$src\World\Materials\MaterialType.cs"
$matRegFile  = "$src\World\Materials\MaterialRegistry.cs"

$allMats    = Get-EnumValues $matTypeFile "MaterialType"
$matRegText = Read-UTF8 $matRegFile

$missMat = $allMats | Where-Object { $_ -ne 'Air' -and $matRegText -notmatch "MaterialType\.$_\b" }
if ($missMat) { Fail "MaterialRegistry missing: $($missMat -join ', ')" }
else          { Pass "MaterialRegistry registers all MaterialTypes" }

# ----------------------------------------------------------------
Head "4. Dangerous API usage"

$allCs = Get-ChildItem -Recurse -Filter "*.cs" $src

$badLines = @()
foreach ($f in $allCs) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'GetLocalMousePosition\(\)') {
            $badLines += [pscustomobject]@{ File = $f.Name; Line = $i+1 }
        }
    }
}
# TileWorldRenderer.cs uses GetLocalMousePosition() for paint brush (correct in Node2D local space).
# Any usage in Main.cs or other non-renderer files is the dangerous pattern.
$dangerLines = $badLines | Where-Object { $_.File -ne 'TileWorldRenderer.cs' }
$safeLines   = $badLines | Where-Object { $_.File -eq 'TileWorldRenderer.cs' }
if ($dangerLines.Count -gt 0) {
    foreach ($b in $dangerLines) { Fail "GetLocalMousePosition() in non-renderer at $($b.File):$($b.Line)" }
} else {
    Pass "No dangerous GetLocalMousePosition() calls (TileWorldRenderer paint brush usage is safe)"
}
if ($safeLines.Count -gt 0) {
    Warn "GetLocalMousePosition() in TileWorldRenderer (paint brush, intentional): $($safeLines.Count) call(s)"
}

# ----------------------------------------------------------------
Head "5. InputBindings — all actions referenced in Main.cs"

$ibFile   = "$src\InputBindings.cs"
$mainFile = "$src\Main.cs"
$ibText   = Read-UTF8 $ibFile
$mainText = Read-UTF8 $mainFile

$actionConsts = [System.Text.RegularExpressions.Regex]::Matches(
    $ibText, 'public const string \w+\s*=\s*"([^"]+)"'
) | ForEach-Object { $_.Groups[1].Value }

$unused = $actionConsts | Where-Object {
    $mainText -notmatch [System.Text.RegularExpressions.Regex]::Escape("""$_""") -and
    $mainText -notmatch [System.Text.RegularExpressions.Regex]::Escape("InputBindings.")
}
if ($unused -and $unused.Count -gt 0) {
    Warn "Actions not yet referenced in Main.cs (may be normal): $($unused -join ', ')"
} else {
    Pass "InputBindings actions appear referenced in Main.cs"
}

# ----------------------------------------------------------------
Head "6. Tech debt count (TODO / FIXME / pending balance)"

$debtItems = @()
foreach ($f in $allCs) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '(TODO|FIXME|HACK)') {
            $debtItems += [pscustomobject]@{ File = $f.Name; Line = $i+1; Text = $lines[$i].Trim() }
        }
    }
}
if ($debtItems.Count -gt 0) {
    Warn "Tech debt markers found: $($debtItems.Count) (use -ShowDetails to list)"
    if ($ShowDetails) {
        $debtItems | ForEach-Object { Write-Host "      $($_.File):$($_.Line)  $($_.Text)" }
    }
} else {
    Pass "No TODO/FIXME/HACK markers"
}

# ----------------------------------------------------------------
Write-Host ""
Write-Host "=============================="
if ($fail -eq 0) {
    Write-Host "  PASS $pass   FAIL $fail" -ForegroundColor Green
} else {
    Write-Host "  PASS $pass   FAIL $fail" -ForegroundColor Red
}
Write-Host "=============================="
Write-Host ""

if ($fail -gt 0) { exit 1 } else { exit 0 }
