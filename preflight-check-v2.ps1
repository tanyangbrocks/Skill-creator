# SkillCreator preflight check v2 — Windows PowerShell 5.1 compatible
# Run: powershell -ExecutionPolicy Bypass -File preflight-check-v2.ps1 [-ShowDetails]
param([switch]$ShowDetails)

$root = $PSScriptRoot
$src  = "$root\Scripts"
$pass = 0
$fail = 0
$warn = 0

function Pass($msg) { Write-Host "  OK  $msg" -ForegroundColor Green;  $script:pass++ }
function Fail($msg) { Write-Host "  NG  $msg" -ForegroundColor Red;    $script:fail++ }
function Warn($msg) { Write-Host "  WW  $msg" -ForegroundColor Yellow; $script:warn++ }
function Head($msg) { Write-Host "`n-- $msg --" -ForegroundColor Cyan  }

# Extract enum member names from a C# source file
function Get-EnumValues($file, $enumName) {
    $text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
    $opts = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $pat  = "enum\s+$enumName\s*(?::\s*\w+\s*)?\{([^}]+)\}"
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
# TotemDone/Hit/Fizzle — handled as conditionType params in EvalCondition, not as compiler cases
# SequentialGate — blocked: BlockNode has no ExtraBranches
# EndOfChain / Detect* / OnEffect* — blocked: need event infrastructure
$spExclude = @('TotemDone','TotemHit','TotemFizzle',
               'SequentialGate',
               'EndOfChain',
               'DetectProjectile','DetectAttack','DetectStatusChange',
               'OnEffectStart','OnEffectEnd')
$missSP = $allBTs | Where-Object { $spText -notmatch "BlockType\.$_\b" -and $_ -notin $spExclude }
if ($missSP) { Warn "SpellCompiler does not reference (may be handled elsewhere): $($missSP -join ', ')" }
else         { Pass "SpellCompiler references all implemented BlockTypes ($($spExclude.Count) pending blocked)" }

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
Head "6. Tech debt count (TODO / FIXME / HACK / STUB)"

$debtTodo  = @()
$debtStub  = @()
foreach ($f in $allCs) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        # STUB takes priority over TODO (a line can match both; count it as STUB)
        if ($ln -match '\bSTUB\b') {
            $debtStub += [pscustomobject]@{ File = $f.Name; Line = $i+1; Text = $ln.Trim() }
        } elseif ($ln -match '(TODO|FIXME|HACK)') {
            $debtTodo += [pscustomobject]@{ File = $f.Name; Line = $i+1; Text = $ln.Trim() }
        }
    }
}

$totalDebt = $debtStub.Count + $debtTodo.Count
if ($debtStub.Count -gt 0) {
    Warn "STUB markers (critical — unimplemented path): $($debtStub.Count)"
    if ($ShowDetails) {
        $debtStub | ForEach-Object { Write-Host "      [STUB] $($_.File):$($_.Line)  $($_.Text)" -ForegroundColor Red }
    }
}
if ($debtTodo.Count -gt 0) {
    Warn "TODO/FIXME/HACK markers: $($debtTodo.Count) (use -ShowDetails to list)"
    if ($ShowDetails) {
        $debtTodo | ForEach-Object { Write-Host "      $($_.File):$($_.Line)  $($_.Text)" }
    }
}
if ($totalDebt -eq 0) {
    Pass "No TODO/FIXME/HACK/STUB markers"
}

# ----------------------------------------------------------------
Head "7. WorldScale — renderer bare float + extended magic-number scan"

# 7a: Original check — scan only tile-to-Godot conversion layer files for bare floats
$renderFiles = @(
    "$src\World\TileWorldRenderer3D.cs",
    "$src\World\CameraController.cs"
) | Where-Object { Test-Path $_ }

# Lines containing these are considered safe (already reference TileSize, or are non-tile-unit camera params)
$tsSafe = 'TileSize|WorldScale\.|[^A-Za-z]T[^A-Za-z]|\bT\s*\*|\*\s*T\b|Fov|MouseSens|FpEye|IsoYaw|IsoPitch|PerspFov|ProjectRay|UnprojectPos|ProjectPos|GetCenter|Relative'

$v7 = @()
foreach ($fp in $renderFiles) {
    $fname   = [System.IO.Path]::GetFileName($fp)
    $content = [System.IO.File]::ReadAllLines($fp, [System.Text.Encoding]::UTF8)
    $lineNum  = 0
    foreach ($ln in $content) {
        $lineNum++
        if ($ln -match '^\s*//') { continue }
        if ($ln -match $tsSafe)  { continue }
        if ($ln -match 'new\s+Vector[23]\s*\(' -and $ln -match '\b[1-9]\d*\.?\d*f\b') {
            $v7 += ($fname + ':' + $lineNum + '  ' + $ln.Trim())
        }
    }
}
$v7cnt = $v7.Count
if ($v7cnt -gt 0) {
    Warn ('Renderer Vector3/2 bare float (needs TileSize): ' + $v7cnt + ' hit(s)')
    $v7 | ForEach-Object { Write-Host ('      ' + $_) -ForegroundColor Yellow }
} else {
    Pass 'Renderer Vector3/2 — no bare Godot-unit floats'
}

# 7b: Extended — scan Main.cs and MapGenerator3D.cs for magic numbers that match WorldScale constants
# WorldScale (Grain=16): PlayerH=32, PlayerW=16, WorldH=1600, WorldW=3200, WorldD=3200
# Also flag 6400 (2×WorldW) as likely a magic-number mistake.
# Numbers that are legitimately raw (e.g. UI pixel offsets) are excluded via context filters.
$wsExtFiles = @(
    "$src\Main.cs",
    "$src\World\MapGenerator3D.cs"
) | Where-Object { Test-Path $_ }

# Known WorldScale magic values (tile counts that should reference constants)
$wsMagicPat = '\b(1600|3200|6400|3264|6528)\b'
# Safe contexts: comments, string literals, WorldScale references, chunk size math
$wsSafeCtx  = '^\s*//|WorldScale\.|Chunk3D\.|".*\b(1600|3200)\b.*"|px|Pixel|pixel|Width.*=.*\d|Height.*=.*\d|MinSize|viewport|screen'

$v7b = @()
foreach ($fp in $wsExtFiles) {
    $fname   = [System.IO.Path]::GetFileName($fp)
    $lines   = [System.IO.File]::ReadAllLines($fp, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        if ($ln -match '^\s*//')         { continue }  # skip comments
        if ($ln -match 'WorldScale\.')   { continue }  # already uses constant
        if ($ln -match '"[^"]*\b(1600|3200|6400)\b[^"]*"') { continue }  # inside string literal
        if ($ln -match $wsMagicPat) {
            $v7b += [pscustomobject]@{ File = $fname; Line = $i+1; Text = $ln.Trim() }
        }
    }
}
if ($v7b.Count -gt 0) {
    Warn "Magic tile-coordinate numbers in Main/MapGen (should use WorldScale.*): $($v7b.Count) hit(s)"
    if ($ShowDetails) {
        $v7b | ForEach-Object { Write-Host "      $($_.File):$($_.Line)  $($_.Text)" -ForegroundColor Yellow }
    } else {
        Write-Host "      (run with -ShowDetails to see locations)" -ForegroundColor Yellow
    }
} else {
    Pass 'No bare WorldScale magic-numbers in Main.cs / MapGenerator3D.cs'
}

# ================================================================
Head "8. OpCode completeness — ExecutionLoop switch coverage"

$opCodeFile  = "$src\AbilitySystem\VM\OpCode.cs"
$execLoopFile = "$src\AbilitySystem\VM\ExecutionLoop.cs"

if (-not (Test-Path $opCodeFile)) {
    Warn "OpCode.cs not found — skipping check 8"
} elseif (-not (Test-Path $execLoopFile)) {
    Warn "ExecutionLoop.cs not found — skipping check 8"
} else {
    $allOps      = Get-EnumValues $opCodeFile "OpCode"
    $execText    = Read-UTF8 $execLoopFile

    # Find all "case OpCode.X:" patterns in ExecutionLoop
    $caseMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $execText, 'case\s+OpCode\.(\w+)\s*:'
    )
    $handledOps  = $caseMatches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

    $missingOps = $allOps | Where-Object { $_ -notin $handledOps }
    if ($missingOps) {
        Fail "OpCode enum values not handled in ExecutionLoop switch ($($missingOps.Count) missing): $($missingOps -join ', ')"
    } else {
        Pass "ExecutionLoop handles all $($allOps.Count) OpCode values"
    }
}

# ================================================================
Head "9. DestroyReason completeness — DroppedItemManager coverage"

$destroyFile  = "$src\World\DestroyReason.cs"
$droppedFile  = "$src\World\Items\DroppedItemManager.cs"

if (-not (Test-Path $destroyFile)) {
    Warn "DestroyReason.cs not found — skipping check 9"
} elseif (-not (Test-Path $droppedFile)) {
    Warn "DroppedItemManager.cs not found — skipping check 9"
} else {
    $allReasons   = Get-EnumValues $destroyFile "DestroyReason"
    $droppedText  = Read-UTF8 $droppedFile

    # Check which DestroyReason values are referenced in DroppedItemManager
    $unhandled = $allReasons | Where-Object {
        $droppedText -notmatch "DestroyReason\.$_\b"
    }

    if ($unhandled) {
        # These are WARN (not FAIL) — unhandled reasons silently produce no drop,
        # which may be intentional (e.g. Collapse not yet implemented).
        Warn "DestroyReason values not referenced in DroppedItemManager ($($unhandled.Count)): $($unhandled -join ', ')"
        Write-Host "      Note: unhandled reasons produce no drops and no fragments — verify this is intentional." -ForegroundColor Yellow
    } else {
        Pass "DroppedItemManager references all $($allReasons.Count) DestroyReason values"
    }
}

# ================================================================
Head "10. SpawnCategory completeness — MobSpawnController coverage"

$spawnCatFile  = "$src\World\SpawnCategory.cs"
$mobSpawnFile  = "$src\World\MobSpawnController.cs"

if (-not (Test-Path $spawnCatFile)) {
    Warn "SpawnCategory.cs not found — skipping check 10"
} elseif (-not (Test-Path $mobSpawnFile)) {
    Warn "MobSpawnController.cs not found — skipping check 10"
} else {
    $allCats     = Get-EnumValues $spawnCatFile "SpawnCategory"
    $mobText     = Read-UTF8 $mobSpawnFile

    $unhandledCats = $allCats | Where-Object {
        $mobText -notmatch "SpawnCategory\.$_\b"
    }

    if ($unhandledCats) {
        # WARN: Specific/Boss are noted as "暫未定義" in the enum comments
        Warn "SpawnCategory values not referenced in MobSpawnController ($($unhandledCats.Count)): $($unhandledCats -join ', ')"
        Write-Host "      Note: unhandled categories are never spawned or despawned by MobSpawnController." -ForegroundColor Yellow
    } else {
        Pass "MobSpawnController references all $($allCats.Count) SpawnCategory values"
    }
}

# ================================================================
Head "11. ManaType / ManaTypeRegistry sync"

$manaTypeFile = "$src\AbilitySystem\Data\ManaType.cs"
$manaRegFile  = "$src\AbilitySystem\Data\ManaTypeRegistry.cs"

if (-not (Test-Path $manaTypeFile)) {
    Warn "ManaType.cs not found — skipping check 11"
} elseif (-not (Test-Path $manaRegFile)) {
    Warn "ManaTypeRegistry.cs not found — skipping check 11"
} else {
    $manaRegText = Read-UTF8 $manaRegFile

    # ManaType is a record (not an enum). Extract registered IDs via "new(\d+," pattern.
    # Registry declares IDs 1-18 for W-6A base types; W-13 composite types are registered dynamically.
    $regIdMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $manaRegText, 'new\s*\(\s*(\d+)\s*,'
    )
    $registeredIds = $regIdMatches | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique

    # Extract registered keys (string keys like "wu_dao", "xian_dao" ...)
    $keyMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $manaRegText, 'Register\s*\(new\s*\([^)]+\)\s*\)'
    )

    # Count expected base types (IDs 1-18, per W-6A spec in ManaTypeRegistry header comment)
    $expectedBaseCount = 18
    $baseIds           = $registeredIds | Where-Object { $_ -ge 1 -and $_ -le $expectedBaseCount }
    $missingBaseIds    = 1..$expectedBaseCount | Where-Object { $_ -notin $baseIds }

    # Check for duplicate IDs (would cause silent overwrite in dictionary)
    $dupeIds = $registeredIds | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name }

    $anyFail = $false
    if ($missingBaseIds) {
        Fail "ManaTypeRegistry missing base ManaType IDs (1-18): $($missingBaseIds -join ', ')"
        $anyFail = $true
    }
    if ($dupeIds) {
        Fail "ManaTypeRegistry has duplicate ID registrations: $($dupeIds -join ', ')"
        $anyFail = $true
    }
    if (-not $anyFail) {
        Pass "ManaTypeRegistry registers all $expectedBaseCount base ManaType IDs (1-$expectedBaseCount); $($registeredIds.Count) total"
    }

}

# ================================================================
Head "12. SaveSystem field sync — CharacterSaveData vs FlowSaveSystem"

$charSaveFile = "$src\GameFlow\CharacterSaveData.cs"
$flowSaveFile = "$src\GameFlow\FlowSaveSystem.cs"

if (-not (Test-Path $charSaveFile)) {
    Warn "CharacterSaveData.cs not found — skipping check 12"
} elseif (-not (Test-Path $flowSaveFile)) {
    Warn "FlowSaveSystem.cs not found — skipping check 12"
} else {
    $charSaveText = Read-UTF8 $charSaveFile
    $flowText     = Read-UTF8 $flowSaveFile

    # Extract public properties/fields from CharacterSaveData (get/set properties)
    $propMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $charSaveText,
        'public\s+\S+\s+(\w+)\s*\{[^}]*get[^}]*set[^}]*\}'
    )
    $allFields = $propMatches | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne '' }

    # FlowSaveSystem uses System.Text.Json serialization for the whole Dto object,
    # which contains List<CharacterSaveData>. Since serialization is automatic (no manual
    # field-by-field access), we check for any fields that are manually referenced in
    # FlowSaveSystem — if a field is read/written by name on only one side, that's a sync risk.
    # We also detect fields that appear ONLY in CharacterSaveData but are NEVER mentioned anywhere
    # in FlowSaveSystem (could indicate a forgotten field in a manual mapping path).

    $flowSaveOnlySave  = @()  # referenced in Save/SaveCharacter but not in Load
    $flowSaveOnlyLoad  = @()  # referenced in Load but not in Save/SaveCharacter
    $flowNotMentioned  = @()  # not mentioned at all in FlowSaveSystem

    foreach ($field in $allFields) {
        $inFlow = $flowText -match "\b$field\b"
        if (-not $inFlow) {
            $flowNotMentioned += $field
        }
        # Note: because FlowSaveSystem uses whole-object JSON serialization via Dto,
        # individual field names not appearing in FlowSaveSystem.cs is expected and OK.
        # What IS a problem is if a field appears in a manual mapping somewhere on only one side.
        # With the current architecture (all-automatic), this check mainly guards against
        # future refactors that add manual mapping without updating both directions.
    }

    # Detect manual per-field assignments (e.g. "character.Level = ..." or ".Level =")
    $manualAssign = [System.Text.RegularExpressions.Regex]::Matches(
        $flowText, '(?:character|chars?)\s*\.\s*(\w+)\s*='
    ) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

    $manualRead = [System.Text.RegularExpressions.Regex]::Matches(
        $flowText, '(?:character|chars?)\s*\.\s*(\w+)\b'
    ) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

    # Fields that are written but not read back (save-only manual access)
    $writeNotRead = $manualAssign | Where-Object { $_ -notin $manualRead -and $_ -in $allFields }
    # Fields that are read but not written (load-only manual access)
    $readNotWrite = $manualRead  | Where-Object { $_ -notin $manualAssign -and $_ -in $allFields }

    $anyFieldIssue = $false
    if ($writeNotRead) {
        Warn "CharacterSaveData fields manually written (save) but never read back in FlowSaveSystem: $($writeNotRead -join ', ')"
        $anyFieldIssue = $true
    }
    if ($readNotWrite) {
        Warn "CharacterSaveData fields manually read in FlowSaveSystem but never written (possible missing save path): $($readNotWrite -join ', ')"
        $anyFieldIssue = $true
    }
    if (-not $anyFieldIssue) {
        Pass "FlowSaveSystem field sync OK ($($allFields.Count) CharacterSaveData fields; JSON serialization is symmetric)"
    }
}

# ================================================================
Head "13. SpawnFragments — DestroyReason branch exhaustiveness"

# SpawnFragments uses a binary branch: Mining vs everything else.
# Verify that newly added DestroyReasons are at least acknowledged in the comment
# (i.e. the function's logic is intentional and documented, not accidentally narrowed).
$droppedFile2 = "$src\World\Items\DroppedItemManager.cs"
if (-not (Test-Path $droppedFile2)) {
    Warn "DroppedItemManager.cs not found — skipping check 13"
} else {
    $dimText2  = Read-UTF8 $droppedFile2
    $allReasons2 = Get-EnumValues "$src\World\DestroyReason.cs" "DestroyReason"

    # SpawnFragments currently only branches on Mining; check if it covers all non-Mining reasons
    # by looking for a default/else path.
    $hasElse = $dimText2 -match 'SpawnFragments[^}]+\?[^}]+:[^}]+'   # ternary covers else

    # Count how many DestroyReason values are referenced inside SpawnFragments specifically
    # (crude: look for DestroyReason.X between the SpawnFragments method open brace and close brace)
    $opts2 = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $sfMethod = [System.Text.RegularExpressions.Regex]::Match(
        $dimText2,
        'SpawnFragments\s*\([^)]+\)\s*\{(.+?)\n    \}',
        $opts2
    )
    $sfBody = if ($sfMethod.Success) { $sfMethod.Groups[1].Value } else { '' }

    $sfReasons = $allReasons2 | Where-Object { $sfBody -match "DestroyReason\.$_\b" }
    $sfMissing = $allReasons2 | Where-Object { $sfBody -notmatch "DestroyReason\.$_\b" }

    if ($sfMissing.Count -gt 0) {
        # This is a warn, not fail — the else branch is a catch-all, but un-named reasons
        # might not get the correct fragment rate.
        Warn "SpawnFragments has no explicit case for DestroyReason: $($sfMissing -join ', ') (falls through to default random rate — verify intentional)"
    } else {
        Pass "SpawnFragments explicitly handles all DestroyReason values"
    }
}

# ================================================================
Head "14. AbilitySystem SaveSystem — ManaSlot / ManaType round-trip"

$manaSlotFile = "$src\AbilitySystem\Data\ManaSlot.cs"
$abilitySave  = "$src\AbilitySystem\SaveSystem.cs"

if (-not (Test-Path $manaSlotFile)) {
    Warn "ManaSlot.cs not found — skipping check 14"
} elseif (-not (Test-Path $abilitySave)) {
    Warn "AbilitySystem\SaveSystem.cs not found — skipping check 14"
} else {
    $manaSlotText  = Read-UTF8 $manaSlotFile
    $abSaveText    = Read-UTF8 $abilitySave

    # Extract public properties from ManaSlot
    $msPropMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $manaSlotText,
        'public\s+\S+\s+(\w+)\s*(?:\{[^}]*\}|;)'
    )
    $msFields = $msPropMatches | ForEach-Object { $_.Groups[1].Value } |
                Where-Object { $_ -ne '' -and $_ -notmatch '^(get|set)$' }

    # Check that ManaSlot is at all mentioned in SaveSystem
    $manaSlotMentioned = $abSaveText -match '\bManaSlot\b'
    if (-not $manaSlotMentioned) {
        Warn "ManaSlot is not referenced in AbilitySystem\SaveSystem.cs — ManaSlot data may not be persisted"
    } else {
        Pass "ManaSlot is referenced in AbilitySystem\SaveSystem.cs"
    }

    # Verify ManaTypeRegistry is accessible / mentioned in save path
    $manaRegMentioned = $abSaveText -match '\bManaTypeRegistry\b|\bManaType\b'
    if (-not $manaRegMentioned) {
        Warn "ManaTypeRegistry / ManaType not referenced in AbilitySystem\SaveSystem.cs — ManaType key serialization may be missing"
    } else {
        Pass "ManaType/ManaTypeRegistry referenced in AbilitySystem\SaveSystem.cs"
    }
}

# ================================================================
Write-Host ""
Write-Host "========================================"
$summaryColor = if ($fail -eq 0) { 'Green' } else { 'Red' }
Write-Host "  PASS $pass   WARN $warn   FAIL $fail" -ForegroundColor $summaryColor
Write-Host "========================================"
Write-Host ""

if ($fail -gt 0) { exit 1 } else { exit 0 }
