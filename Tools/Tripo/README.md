# Tripo asset pipeline (Last Light)

Text prompt -> Tripo AI -> FBX -> `Assets/LastLight/Art/Tripo/<id>/` -> Unity prefab.
Claude Code agent for this workflow: `.claude/agents/tripo-modeler.md`.

## Files

| File | Purpose |
| --- | --- |
| `assets.json` | Manifest: one entry per model (prompt, negative prompt, model, face budget, texture size, target size in metres, rig/animations, Unity prefab name), shared style, pricing table |
| `generate.ps1` | Generates one asset (or `all`) from the manifest, with `-DryRun`, `-List`, `-Stage`, `-Force` |
| `inspect_model.py` | Blender script: triangles, bbox size, pivot height, textures, bones, clips, scale factor |
| `runs/<id>/` | Created on real runs: raw CLI downloads, `<stage>.result.json`, `state.json` (task ids). Outside Assets so Unity ignores it |

## 1. Setup

Requires Node.js 20+.

```powershell
npm install -g tripo-cli      # installed here: tripo-cli 0.8.0
tripo --version
tripo update --check          # optional: check for a newer CLI
```

If PowerShell blocks the `tripo` shim, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
(no admin needed), or call `tripo.cmd`. `generate.ps1` calls the CLI through `node` directly, so it is
not affected; run it with `-ExecutionPolicy Bypass` as shown below.

## 2. Authentication (do this yourself; never paste the key into chat or code)

Pick ONE option. Your account site matters: `ov` = international (developers.tripo3d.ai),
`cn` = China mainland (developers.tripo3d.com).

**A. Browser login (recommended, stores the key under `%USERPROFILE%\.tripo`)**

```powershell
tripo login --region ov
```

Sign in on the page it opens (or the URL it prints), enter the one-time code, choose an API key, click
Authorize. The key is saved locally for the CLI.

**B. Environment variable (persistent, current user)**

1. Create a key at https://developers.tripo3d.ai (API keys start with `tsk_`).
2. Windows Settings > System > About > Advanced system settings > Environment Variables > User
   variables > New: name `TRIPO_API_KEY`, value = your key. (China-site keys: also add
   `TRIPO_REGION` = `cn`.)
3. Restart terminals and Claude Code so they inherit it.

**Check** (prints credits only, never the key):

```powershell
tripo balance --json     # exit 0 = configured; exit 3 = "no API key configured"
```

`tripo doctor` also checks node, network and balance (it shows a masked key; do not share its output).
Remove a stored key with `tripo logout`.

## 3. Dry run (free, offline, no key needed)

```powershell
cd D:\Unity\Pico\PicoProject
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -List
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id jeep -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id all -DryRun
```

Prints the exact `tripo make ...` command, the CLI's own validation (`tripo make ... --dry-run --json`:
no upload, no submit, no credits) and the credit estimate (generation from the CLI, chain steps from
the manifest pricing table). Animation stages show `<base-rig-task-id>` until the base exists.

## 4. Generate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id jeep
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id zombie_a            # base + walk + run
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id zombie_a -Stage run # one stage
```

- Each run blocks until the files are downloaded (often several minutes; the CLI watches up to 30 min).
- Static props: generate (P2, detailed texture) -> convert to FBX with `texture_size` and
  `pivot_to_center_bottom=true`.
- Characters: generate -> rig-check (free) -> rig (`spec=unity` Humanoid, FBX) = base stage; then one
  retarget per clip (`animate_in_place=true`, FBX) = `<Prefab>@walk.fbx`, `<Prefab>@run.fbx`.
- Already generated stages are skipped. `-Force` regenerates (a new paid task).
- Results: `Assets/LastLight/Art/Tripo/<id>/<Prefab>.fbx`, `<Prefab>_preview.png`, textures, clip FBXs.
  Raw output and task ids: `Tools/Tripo/runs/<id>/`.
- Failure exit codes: 3 auth, 4 credits (`tripo topup`), 5 content policy (change prompt), 6 task
  failed (refunded) or timeout (`tripo task watch <id> --download`, do not rerun), 7 network (check
  `tripo task list` / `tripo balance` before rerunning; recover downloads with
  `tripo task get <task_id> --download`).

Estimated cost of the whole manifest (list price): about 1500 credits (USD 15). Per asset: 130 for
static props (120 P2 detailed generation + 10 advanced convert); 165 per zombie (120 + 25 rig + 2 x 10
retarget).

## 5. Inspect

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup `
  --python Tools\Tripo\inspect_model.py -- Assets\LastLight\Art\Tripo\jeep\LL_Jeep.fbx 0.10
```

Reports triangles (compare with `face_limit`), bbox size, whether the pivot is at the base, texture
sizes, bones/clips, and the uniform scale for the target size.

## 6. Unity import (summary; full steps in `.claude/agents/tripo-modeler.md`)

1. FBX importer: Scale Factor so the model matches `target_size_m`; Read/Write off; Mesh Compression
   Low; tangents Mikktspace.
2. Materials tab: Extract Textures, Extract Materials into `Materials/`; URP Lit (Simple Lit for horde
   and foliage); Metallic 0, Smoothness 0.1-0.3; GPU Instancing on for props.
3. Textures: Max Size from the manifest, Android ASTC 6x6, normal maps typed as Normal map.
4. Characters: Humanoid avatar from `<Prefab>.fbx`, clips copy the avatar; Loop Time, bake root
   transforms (clips are in place).
5. Prefab `<Prefab>.prefab`: empty root at the base (colliders here), model child facing +Z.
6. Replace placeholders by adding the model as a child of the placeholder prefab and disabling the
   primitive renderer. Do not edit gameplay scripts or scenes.

## Notes and known ambiguities

- `-o` is a base directory: the CLI writes into `<dir>/tripo-out/<name>-<id8>/`; the script copies from
  the `output_dir` the CLI reports.
- `export_orientation` only offers `+x/-x/+y/-y` and generation-time orientation is not inherited by
  chained steps, so the pipeline leaves it default and fixes facing (+Z) in the prefab.
- A `--then` chain can carry only one retarget animation, hence one stage per clip.
- The P1 model has no published price; the CLI does not estimate it (not zero).
- Unity does not import GLB natively here (the installed UnityGLTF package is an exporter-era build),
  so everything is converted to FBX.
