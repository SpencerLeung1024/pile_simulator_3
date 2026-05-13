# Pile Simulator 3 — Agent Notes

## Stack
- Godot 4.7.dev3, Forward+, C# (.NET 8), Jolt Physics
- Built with Godot.NET.Sdk/4.7.0-dev.3; TargetFramework net8.0
- Open via Godot Editor or `dotnet build`; no custom test/lint/CI setup exists

## Directory Boundaries
| Directory | Purpose |
|-----------|---------|
| `Scripts/` | Godot node scripts (`[GlobalClass]`, `[Export]`, `_Ready`, `_Process`) |
| `DSA/` | Pure data-structure / algorithm code (octree, generator, materials). No Godot node inheritance. |
| `Scenes/` | `.tscn` files; scene tree is wired in the editor, not in code |
| `reports/` | Design docs, algorithm references, and debug dumps |

## Scene-Code Coupling Rules (from `.clinerules`, strictly observed)
- **Do not assign default values to `[Export]` fields in scripts.** The author configures all node parameters in the Godot editor. Leave `[Export]` fields uninitialized.
- **Do not search for scene nodes in `_Ready()`.** All node references must be `[Export]` and assigned in the editor.
- The scene tree should **reflect** the underlying game data; it does not **own** it.
- Fail early, fail loudly. Do not continue with invalid state.

## Architecture
- `World.tscn` → `World.cs` (root) → `Asteroid` + `FreeCamController` + `UI`
- `Asteroid.cs` owns the sparse voxel octree (`DSA/Octree.cs`) and the `AsteroidGenerator`.
- Two traversal modes (toggleable in UI):
  - `QueryForLOD`: Barnes-Hut-style theta culling. Scales poorly (~r³); used for baseline comparison.
  - `SurfaceTraversal`: Flood-fill over exposed surface starting from seed points. Intended to scale near-surface.
- Near nodes (real voxels) → `StaticRock.tscn` instances via object pool.
- Far nodes (internal octree nodes) → `MultiMeshRock.tscn` / `MultiMeshInstance3D`.

## Key Implementation Details
- **Octree node identity for pooling:** `Asteroid.cs` rounds `node.Center * 1000` to `Vector3I` as the dictionary key for static rocks. Changing coordinate scaling breaks the pool.
- **Neighbor culling holes:** At coarse LOD, `GetExposedFaces` can falsely report a node as enclosed if a same-height neighbor’s center-sampled material is solid but it contains empty children. `SurfaceTraversal` avoids this by drilling to leaf level before walking up.
- **Samet neighbor algorithm:** `Octree.GetFaceNeighbor` uses bit-mirrored path traversal. See `reports/better_traversal/` for reference walkthrough.
- **Consolidation:** `Octree.Consolidate` is a manual, post-order pass that collapses uniform subtrees. Must be triggered from UI; it is not automatic.
- **Material enum:** `MaterialEnum.Empty = -1` (matches `GridMap.InvalidCellItem`). Rock=0, Ice=1, Metal=2.

## Singletons
- `Settings.GetSettings()` — shared mutable state (debug text, toggles, slider values)
- `UIController.GetUIController()` — UI node references; throws if called before `_Ready`
- `FreeCamController.GetFreeCamController()` — camera singleton; throws if called before `_Ready`

## Running / Testing
- No unit tests or automated verification exist.
- Validate by opening the project in the Godot Editor and running the main scene (`World.tscn`).
- Use the in-game debug panel (FPS, traversal stats, rock counts) to verify behavior.

## WSL Bridge
- `opencode.ps1` is a PowerShell helper that forwards `opencode` CLI calls into WSL at the current Windows directory. It assumes the WSL username matches `$env:USERNAME`.

## Porting Note (Roo Code → OpenCode)
- `.clinerules` was the previous instruction file. This `AGENTS.md` supersedes it for OpenCode sessions. Retain the editor-assignment and fail-early conventions above.
