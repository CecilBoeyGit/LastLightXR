<#
.SYNOPSIS
  Generate one Last Light asset (or all) from Tools/Tripo/assets.json with the Tripo CLI.

.DESCRIPTION
  Stages per asset:
    base        generation (+ convert to FBX for static props, or rig-check + rig for characters)
    <anim>      one retarget stage per entry in "animations" (characters only), e.g. walk, run

  Downloads land in Tools/Tripo/runs/<id>/<stage>/ (outside Assets), then the model, preview and
  textures are copied to Assets/LastLight/Art/Tripo/<id>/ with Unity-friendly names:
    <Prefab>.fbx, <Prefab>_preview.png, <Prefab>@<anim>.fbx
  Task ids are recorded in Tools/Tripo/runs/<id>/state.json so later stages chain from the base task.

  -DryRun prints the exact command and the credit estimate and runs the CLI's own offline
  validation (tripo make ... --dry-run --json: no network, no credits, no API key needed).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id jeep -DryRun
  powershell -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id jeep
  powershell -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id zombie_a -Stage walk
  powershell -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id all -DryRun
  powershell -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -List
#>
[CmdletBinding()]
param(
    [string]$Id,
    [string]$Stage = 'all',
    [switch]$DryRun,
    [switch]$Force,
    [switch]$List,
    [string]$Manifest
)

$ErrorActionPreference = 'Stop'
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
if (-not $Manifest) { $Manifest = Join-Path $ScriptDir 'assets.json' }

$env:TRIPO_CLIENT_APP      = 'claude'
$env:TRIPO_NO_UPDATE_CHECK = '1'

$m = Get-Content -Raw -Encoding UTF8 $Manifest | ConvertFrom-Json

# ---------------------------------------------------------------- helpers
function Get-TripoCli {
    $cmd = Get-Command tripo.cmd -ErrorAction SilentlyContinue
    $candidates = @()
    if ($cmd) { $candidates += (Join-Path (Split-Path -Parent $cmd.Source) 'node_modules\tripo-cli\dist\cli.js') }
    try { $root = (& npm.cmd root -g 2>$null); if ($root) { $candidates += (Join-Path $root.Trim() 'tripo-cli\dist\cli.js') } } catch {}
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'tripo-cli not found. Install it with: npm install -g tripo-cli'
}

function Format-Cmd([string[]]$a) {
    $parts = foreach ($x in $a) { if ($x -match '[\s,;]') { '"' + $x + '"' } else { $x } }
    'tripo ' + ($parts -join ' ')
}

function Get-Asset([string]$assetId) {
    $a = $m.assets | Where-Object { $_.id -eq $assetId }
    if (-not $a) { throw "Unknown asset id '$assetId'. Use -List to see ids." }
    return $a
}

function Get-Val($obj, [string]$name, $default) {
    if ($null -ne $obj -and $obj.PSObject.Properties[$name] -and $null -ne $obj.$name) { return $obj.$name }
    return $default
}

function Get-Stages($a) {
    $s = @('base')
    foreach ($anim in @(Get-Val $a 'animations' @())) { $s += [string]$anim }
    return $s
}

function Get-RunDir($a)   { Join-Path $ProjectRoot (Join-Path $m.staging_root $a.id) }
function Get-OutDir($a)   { Join-Path $ProjectRoot (Join-Path $m.output_root $a.id) }
function Get-StatePath($a){ Join-Path (Get-RunDir $a) 'state.json' }

function Read-State($a) {
    $p = Get-StatePath $a
    if (Test-Path $p) { return (Get-Content -Raw -Encoding UTF8 $p | ConvertFrom-Json) }
    return [pscustomobject]@{ id = $a.id; stages = [pscustomobject]@{} }
}

function Save-State($a, $state) {
    $dir = Get-RunDir $a
    New-Item -ItemType Directory -Force $dir | Out-Null
    $json = $state | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText((Get-StatePath $a), $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-StageState($state, [string]$stageName) {
    if ($state.stages.PSObject.Properties[$stageName]) { return $state.stages.$stageName }
    return $null
}

# Build the CLI argument list for one stage. $inputOverride replaces the source task id (dry run placeholder).
function Build-Args($a, [string]$stageName, [string]$sourceTaskId) {
    $d = $m.defaults
    $name = 'll-' + ($a.id -replace '_', '-')
    $stageOut = Join-Path (Get-RunDir $a) $stageName
    $list = New-Object System.Collections.Generic.List[string]
    $list.Add('make')

    if ($stageName -eq 'base') {
        $image = Get-Val $a 'input_image' $null
        if ($image) {
            $list.Add((Join-Path $ProjectRoot $image))
        } else {
            $prompt = $a.prompt.Trim()
            $suffix = Get-Val $m.style 'prompt_suffix' ''
            if ($suffix) { $prompt = "$prompt, $suffix" }
            if ($prompt.Length -gt 1024) { throw "$($a.id): prompt is $($prompt.Length) chars (max 1024)" }
            if ($prompt -match '["&|<>^%]') { throw "$($a.id): prompt contains a forbidden character (one of: double quote & | < > ^ %)" }
            $list.Add($prompt)
        }
        $model = Get-Val $a 'model' $d.model
        $list.Add('--model'); $list.Add($model)
        $fl = Get-Val $a 'face_limit' $null
        if ($fl) { $list.Add('-p'); $list.Add("face_limit=$fl") }
        $tq = Get-Val $a 'texture_quality' $d.texture_quality
        if ($tq) { $list.Add('-p'); $list.Add("texture_quality=$tq") }
        if (-not $image) {
            $neg = @((Get-Val $m.style 'negative_base' ''), (Get-Val $a 'negative_prompt' '')) | Where-Object { $_ }
            $negText = ($neg -join ', ')
            if ($negText.Length -gt 255) { throw "$($a.id): combined negative prompt is $($negText.Length) chars (max 255). Shorten style.negative_base or the asset negative_prompt." }
            if ($negText -match '["&|<>^%]') { throw "$($a.id): negative prompt contains a forbidden character" }
            if ($negText) { $list.Add('-p'); $list.Add("negative_prompt=$negText") }
        }
        $extra = Get-Val $a 'params' $null
        if ($extra) { foreach ($p in $extra.PSObject.Properties) { $list.Add('-p'); $list.Add("$($p.Name)=$($p.Value)") } }

        if (Get-Val $a 'rig' $false) {
            $rig = $d.rig
            $rt = Get-Val $a 'rig_type' 'biped'
            $chain = "rig-check,rig:rig_type=$rt,spec=$($rig.spec),out_format=$($rig.out_format)"
        } else {
            $sc = $d.static_chain
            $chain = "convert:format=$($sc.format)"
            $ts = Get-Val $a 'texture_size' $null
            if ($ts) { $chain += ",texture_size=$ts" }
            if ($sc.pivot_to_center_bottom) { $chain += ',pivot_to_center_bottom=true' }
        }
        $list.Add('--then'); $list.Add($chain)
        $list.Add('--name'); $list.Add($name)
    } else {
        $rt = $d.retarget
        $list.Add($sourceTaskId)
        $chain = "retarget:preset:$stageName,animate_in_place=$($rt.animate_in_place.ToString().ToLower()),out_format=$($rt.out_format)"
        $list.Add('--then'); $list.Add($chain)
        $list.Add('--name'); $list.Add("$name-$stageName")
    }
    $list.Add('-o'); $list.Add($stageOut)
    $list.Add('--json'); $list.Add('--yes'); $list.Add('--no-open')
    return ,$list.ToArray()
}

# List-price estimate for one stage (credits). Returns $null when not estimable.
function Get-Estimate($a, [string]$stageName) {
    $p = $m.pricing
    if ($stageName -ne 'base') { return [double]$p.retarget_per_animation }
    $model = Get-Val $a 'model' $m.defaults.model
    $tq = Get-Val $a 'texture_quality' $m.defaults.texture_quality
    $extra = Get-Val $a 'params' $null
    $tier = $tq; if ($tier -eq 'fast') { $tier = 'standard' }
    if ($extra -and $extra.PSObject.Properties['texture'] -and "$($extra.texture)" -eq 'false' -and -not ($extra.PSObject.Properties['pbr'] -and "$($extra.pbr)" -eq 'true')) { $tier = 'none' }
    $gen = $null
    switch -Regex ($model) {
        '^(tripo-p2|p2)'   { $gen = [double]$p.generation.'tripo-p2'.$tier }
        '^(tripo-v3\.1|v3\.1|h3\.1)' {
            $key = 'tripo-v3.1-text'; if (Get-Val $a 'input_image' $null) { $key = 'tripo-v3.1-image' }
            $gen = [double]$p.generation.$key.$tier
            if ($extra) {
                if ("$($extra.smart_low_poly)" -eq 'true') { $gen += $p.v31_addons.smart_low_poly }
                if ("$($extra.quad)" -eq 'true') { $gen += $p.v31_addons.quad }
                if ("$($extra.generate_parts)" -eq 'true') { $gen += $p.v31_addons.generate_parts }
                if ("$($extra.geometry_quality)" -eq 'detailed') { $gen += $p.v31_addons.geometry_quality_detailed }
            }
        }
        default { $gen = $null }
    }
    if ($null -eq $gen) { return $null }
    if (Get-Val $a 'rig' $false) { $chain = [double]$p.rig_check + [double]$p.rig }
    else {
        $adv = (Get-Val $a 'texture_size' $null) -or $m.defaults.static_chain.pivot_to_center_bottom
        $chain = if ($adv) { [double]$p.convert_advanced } else { [double]$p.convert_basic }
    }
    return $gen + $chain
}

function Invoke-Tripo([string]$cli, [string[]]$cliArgs) {
    # stderr (plan card, progress) streams straight to the console; stdout is the JSON contract.
    $out = & node $cli @cliArgs
    $code = $LASTEXITCODE
    $line = @($out | Where-Object { $_ -and $_.Trim().StartsWith('{') }) | Select-Object -Last 1
    $obj = $null
    if ($line) { try { $obj = $line | ConvertFrom-Json } catch { $obj = $null } }
    return [pscustomobject]@{ code = $code; json = $obj; raw = ($out -join "`n") }
}

function Copy-Artifacts($a, [string]$stageName, $result) {
    $outDir = Get-OutDir $a
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $prefab = $a.unity.prefab
    $copied = @()
    $src = $result.output_dir
    if (-not $src -or -not (Test-Path $src)) { Write-Warning "output_dir missing in result; nothing copied"; return $copied }
    $model = $result.model_file
    foreach ($f in Get-ChildItem -File $src) {
        if ($f.Name -eq 'task.json') { continue }
        $isModel = $model -and ((Resolve-Path $model).Path -eq $f.FullName)
        if ($stageName -eq 'base') {
            if ($isModel) { $dest = "$prefab$($f.Extension)" }
            elseif ($f.Name -eq 'preview.png') { $dest = "${prefab}_preview.png" }
            else { $dest = $f.Name }
        } else {
            if (-not $isModel) { continue }
            $dest = "$prefab@$stageName$($f.Extension)"
        }
        Copy-Item -Force $f.FullName (Join-Path $outDir $dest)
        $copied += (Join-Path $m.output_root (Join-Path $a.id $dest))
    }
    return $copied
}

# ---------------------------------------------------------------- list
if ($List) {
    $total = 0.0
    foreach ($a in $m.assets) {
        if (Get-Val $a 'procedural' $false) { "{0,-18} procedural (no Tripo generation)  -> {1}" -f $a.id, $a.unity.prefab; continue }
        $sum = 0.0; foreach ($s in (Get-Stages $a)) { $e = Get-Estimate $a $s; if ($null -ne $e) { $sum += $e } }
        $flag = if (Get-Val $a 'enabled' $true) { '' } else { ' (disabled)' }
        "{0,-18} {1,-10} faces {2,-6} stages: {3,-16} est {4,5} credits -> {5}{6}" -f $a.id, (Get-Val $a 'model' $m.defaults.model), $a.face_limit, ((Get-Stages $a) -join ','), $sum, $a.unity.prefab, $flag
        if (Get-Val $a 'enabled' $true) { $total += $sum }
    }
    "Total for enabled generated assets: $total credits (list price, about USD {0:N2})" -f ($total / 100)
    exit 0
}

if (-not $Id) { throw 'Pass -Id <asset id|all> (or -List).' }

$cli = Get-TripoCli
$targets = if ($Id -eq 'all') { @($m.assets | Where-Object { (Get-Val $_ 'enabled' $true) -and -not (Get-Val $_ 'procedural' $false) }) } else { @(Get-Asset $Id) }
$grand = 0.0
$failed = $false

foreach ($a in $targets) {
    if (Get-Val $a 'procedural' $false) { Write-Host "$($a.id): procedural asset, build it in Unity. $($a.notes)"; continue }
    $stages = Get-Stages $a
    if ($Stage -ne 'all') {
        if ($stages -notcontains $Stage) { throw "$($a.id): unknown stage '$Stage' (valid: $($stages -join ', '))" }
        $stages = @($Stage)
    }
    $state = Read-State $a

    foreach ($s in $stages) {
        Write-Host ''
        Write-Host "=== $($a.id) / $s ===" -ForegroundColor Cyan
        $prev = Get-StageState $state $s
        if ($prev -and $prev.status -eq 'success' -and -not $Force -and -not $DryRun) {
            Write-Host "already generated (task $($prev.task_id)); skipping. Use -Force to regenerate (new paid task)."
            continue
        }
        $baseTask = $null
        if ($s -ne 'base') {
            $b = Get-StageState $state 'base'
            if ($b -and $b.task_id) { $baseTask = [string]$b.task_id }
            elseif (-not $DryRun) { throw "$($a.id): run the base stage first (no rigged base task in state.json)" }
        }
        $est = Get-Estimate $a $s
        $estText = if ($null -eq $est) { 'not estimated (P1/legacy: NOT zero)' } else { "$est credits (list price, about USD {0:N2})" -f ($est / 100) }

        if ($DryRun) {
            $display = Build-Args $a $s $(if ($baseTask) { $baseTask } else { '<base-rig-task-id>' })
            Write-Host 'command:'
            Write-Host ('  ' + (Format-Cmd $display))
            $check = Build-Args $a $s $(if ($baseTask) { $baseTask } else { 'task_dryrun_placeholder' })
            $r = Invoke-Tripo $cli ($check + @('--dry-run'))
            if ($r.json) {
                $ge = if ($null -ne $r.json.generation_credits_estimate) { $r.json.generation_credits_estimate } else { 'n/a' }
                Write-Host ("cli validation: valid={0} model={1} cli_generation_estimate={2}" -f $r.json.valid, $r.json.model, $ge)
                foreach ($w in @($r.json.warnings)) { if ($w -and $w -notmatch 'placeholder|unverified') { Write-Host "  warning: $w" -ForegroundColor Yellow } }
                foreach ($e in @($r.json.errors))   { if ($e) { Write-Host "  error: $e" -ForegroundColor Red; $failed = $true } }
            } else { Write-Host "cli validation failed (exit $($r.code)): $($r.raw)" -ForegroundColor Red; $failed = $true }
            Write-Host "estimated cost (generation + chain): $estText"
            if ($null -ne $est) { $grand += $est }
            continue
        }

        $cliArgs = Build-Args $a $s $baseTask
        Write-Host ('running: ' + (Format-Cmd $cliArgs))
        Write-Host "estimated cost: $estText"
        $r = Invoke-Tripo $cli $cliArgs
        $runDir = Get-RunDir $a
        New-Item -ItemType Directory -Force $runDir | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $runDir "$s.result.json"), $r.raw, (New-Object System.Text.UTF8Encoding($false)))
        if ($r.code -ne 0 -or -not $r.json -or $r.json.status -ne 'success') {
            Write-Host "FAILED (exit $($r.code)). Result saved to $(Join-Path $runDir "$s.result.json")" -ForegroundColor Red
            switch ($r.code) {
                3 { Write-Host 'auth error: set TRIPO_API_KEY or run tripo login (see Tools/Tripo/README.md), then tripo doctor' }
                4 { Write-Host 'insufficient credits: run tripo topup' }
                5 { Write-Host 'content policy: change the prompt' }
                6 { Write-Host 'task failed (refunded) or timed out (still running: tripo task watch <id> --download; do NOT rerun)' }
                7 { Write-Host 'network: check tripo balance / tripo task list before rerunning; if the error JSON has task_id use tripo task get <id> --download' }
            }
            $failed = $true
            break
        }
        $copied = Copy-Artifacts $a $s $r.json
        $entry = [pscustomobject]@{
            status = 'success'; task_id = $r.json.task_id; source_task_id = $r.json.source_task_id
            credits_consumed = $r.json.credits_consumed; output_dir = $r.json.output_dir
            unity_files = $copied; generated_at = (Get-Date).ToString('s')
        }
        if ($state.stages.PSObject.Properties[$s]) { $state.stages.$s = $entry } else { $state.stages | Add-Member -NotePropertyName $s -NotePropertyValue $entry }
        Save-State $a $state
        Write-Host ("done: task {0}, credits consumed {1}" -f $r.json.task_id, $r.json.credits_consumed) -ForegroundColor Green
        foreach ($c in $copied) { Write-Host "  -> $c" }
    }
}

if ($DryRun) { Write-Host ''; Write-Host ("TOTAL estimate: {0} credits (list price, about USD {1:N2}). Dry run only: nothing was submitted." -f $grand, ($grand / 100)) }
if ($failed) { exit 1 }
