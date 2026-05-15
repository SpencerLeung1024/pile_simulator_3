"""
Part 2: Verify the constant C ≈ 8 * (4/3)π and build the complete model.

The naive model for nodes expanded at each level h:
  expansion_radius = 2^h * r
  nodes_in_sphere = (4/3)π * (2^h * r)^3 / (2^h)^3 = (4/3)π * r^3

Each expanded node produces 8 children, so visited children per level ≈ 8 * (4/3)π * r^3.
Total visited ≈ 8 * (4/3)π * r^3 * (number of uncapped levels)
            ≈ 33.51 * r^3 * log2(2a/r)

But this overcounts because:
- The sphere extends above the asteroid surface into empty space
- "Truly empty" pruning removes nodes entirely outside the asteroid
- "Truly solid" pruning removes nodes deep inside

The empirical C ≈ 32 is close to but slightly less than 8*(4/3)π ≈ 33.5,
reflecting the pruning.
"""

import numpy as np

print("=" * 80)
print("CONSTANT ANALYSIS")
print("=" * 80)

C_theory = 8 * (4/3) * np.pi
print(f"\nTheoretical C = 8 * (4/3)π = {C_theory:.4f}")
print(f"Empirical C from r sweep ≈ 32.26")
print(f"Ratio: 32.26 / {C_theory:.2f} = {32.26 / C_theory:.4f}")
print(f"This means pruning eliminates about {(1 - 32.26/C_theory)*100:.1f}% of the theoretical nodes")

# But actually, the Leaf/r^3 ratio is 33.5, very close to C_theory
# So the leaf level (which is on the surface) does scale with the full sphere
# And the internal levels contribute slightly less due to pruning
print(f"\nLeaf/r^3 ≈ 33.5 ≈ C_theory = {C_theory:.2f} (leaves match the full sphere)")
print(f"This makes sense: at height 0, all nodes within radius r are visited")
print(f"(there's no theta check for leaves, they're terminal regardless)")

print("\n" + "=" * 80)
print("COMPLETE MODEL SUMMARY")
print("=" * 80)

# For the r sweep
print("\n--- MODEL: Visited ≈ C * r^3 * log2(2a/r) ---")
print("--- C ≈ 32 (empirical), C_theory = 8*(4/3)π ≈ 33.5 ---\n")

# Test with both sweeps
r_data = [(10, 359449), (20, 2581441), (30, 8134729), (40, 18396841), (50, 34511169)]
a_val = 10000.0

print("r sweep (a=10000):")
print(f"{'r':>6} | {'Actual':>12} | {'Pred(C=32)':>12} | {'Err%':>7} | {'Pred(theory)':>12} | {'Err%':>7}")
print("-" * 75)
for r, actual in r_data:
    pred32 = 32 * r**3 * np.log2(2*a_val/r)
    pred_th = C_theory * r**3 * np.log2(2*a_val/r)
    err32 = (pred32 - actual) / actual * 100
    err_th = (pred_th - actual) / actual * 100
    print(f"{r:6} | {actual:12} | {pred32:12.0f} | {err32:+6.1f}% | {pred_th:12.0f} | {err_th:+6.1f}%")

a_data = [(20, 257801), (100, 6720265), (500, 16383849), (2000, 24761545), (10000, 34511169)]
r_val = 50.0

print(f"\na sweep (r=50):")
print(f"{'a':>6} | {'Actual':>12} | {'Pred(C=32)':>12} | {'Err%':>7} | {'log2(2a/r)':>10} | {'Notes':>20}")
print("-" * 85)
for a, actual in a_data:
    log_term = np.log2(max(2*a/r_val, 2))
    pred32 = 32 * r_val**3 * log_term
    err32 = (pred32 - actual) / actual * 100
    notes = ""
    if a < r_val:
        notes = "DEGENERATE (a < r)"
    elif a < 2*r_val:
        notes = "NEAR-DEGENERATE"
    print(f"{a:6} | {actual:12} | {pred32:12.0f} | {err32:+6.1f}% | {log_term:10.2f} | {notes:>20}")

# Time prediction
print("\n\n--- TIME MODEL ---")
print("Octree time should be proportional to Visited (constant per-node work)")
r_ms = [(10, 16.21, 359449), (20, 124.83, 2581441), (30, 385.44, 8134729), (40, 864.43, 18396841), (50, 1594.98, 34511169)]
print(f"\n{'r':>6} | {'ms':>10} | {'Visited':>12} | {'ns/node':>10}")
print("-" * 50)
for r, ms, v in r_ms:
    ns_per_node = ms * 1e6 / v
    print(f"{r:6} | {ms:10.2f} | {v:12} | {ns_per_node:10.2f}")

a_ms = [(20, 4.08, 257801), (100, 177.99, 6720265), (500, 659.64, 16383849), (2000, 1100.80, 24761545), (10000, 1594.98, 34511169)]
print(f"\n{'a':>6} | {'ms':>10} | {'Visited':>12} | {'ns/node':>10}")
print("-" * 50)
for a, ms, v in a_ms:
    ns_per_node = ms * 1e6 / v
    print(f"{a:6} | {ms:10.2f} | {v:12} | {ns_per_node:10.2f}")

# Neighbor culling cost analysis
print("\n\n--- NEIGHBOR CULLING COST ---")
print("Base (with culling):     Octree=1594.98ms, Meshes=96.96ms, MultiMesh=176298, FPS=144")
print("No culling:              Octree=1508.59ms, Meshes=6763.21ms, MultiMesh=12862295, FPS=10")
print(f"Culling overhead:        {1594.98-1508.59:.2f} ms ({(1594.98-1508.59)/1508.59*100:.1f}% overhead)")
print(f"Mesh savings:            {6763.21-96.96:.2f} ms ({(6763.21-96.96)/6763.21*100:.1f}% reduction)")
print(f"MultiMesh reduction:     {12862295} -> {176298} ({176298/12862295*100:.2f}%)")
print(f"Net savings:             {(1508.59+6763.21)-(1594.98+96.96):.2f} ms per frame")

# Why r^3 not r^2?
print("\n\n" + "=" * 80)
print("WHY r^3 NOT r^2?")
print("=" * 80)
print("""
The user hoped for r^2 * log(r), reasoning:
  "you need at least r^2 to represent the surface within the realization radius"

This is correct for the OUTPUT (visible surface nodes):
  MultiMesh ≈ 70 * r^1.97 ≈ r^2  (confirmed by fit)
  Static rocks are capped at 10000

But the TRAVERSAL COST is r^3 * log(a/r) because:

1. The Barnes-Hut theta criterion expands all nodes within distance 2^h * r.
   This is a VOLUMETRIC sphere, not a surface patch.

2. At height 0 (leaves), the expansion radius = r. The octree must visit ALL
   cells in a sphere of radius r around the camera: both solid AND empty.
   That's (4/3)π r^3 cells, which is r^3.

3. The surface-only count would be r^2, but the octree doesn't know a cell is
   empty without visiting it. Empty leaves (below the surface) are visited and
   discarded. They cost traversal time even though they produce no output.

4. At higher levels (h > 0), the same r^3 pattern repeats because the expansion
   radius scales with node size (2^h * r), and the node count at that level
   scales inversely with (2^h)^3, so they cancel to r^3 per level.

5. The log(a/r) factor comes from the number of levels that need traversal
   before the expansion sphere encompasses the entire asteroid.

The ONLY way to achieve r^2 * log(r) would be to have the traversal skip
the interior of the asteroid entirely, visiting only surface-adjacent nodes.
This would require a fundamentally different data structure (e.g., a surface
octree that doesn't represent the interior at all).
""")

# What about the TrulyEmpty pruning?
print("--- TrulyEmpty helps but doesn't change the exponent ---")
print("TrulyEmpty nodes:")
te_data = [(10, 3433), (20, 12948), (30, 28221), (40, 50062), (50, 80589)]
for r, te in te_data:
    pct = te / dict(r_data)[r] * 100
    print(f"  r={r}: {te} ({pct:.1f}% of Visited)")
print("""
TrulyEmpty scales as ~r^2.04 (confirmed by fit), so it prunes a constant
fraction of the r^3 volume (the fraction that's far from the surface).
But it doesn't reduce the exponent because there are many mixed nodes
near the surface that can't be pruned.
""")

print("\n" + "=" * 80)
print("OPTIMIZATION OPPORTUNITIES")
print("=" * 80)
print("""
1. SKIP INTERIOR: If we could mark nodes as "interior solid" (all descendants
   are solid), we could skip their traversal. The "TrulySolid" mechanism does
   this but requires children to be realized first. Pre-computing could help.

2. SURFACE OCTREE: Store only surface-adjacent nodes. This would achieve the
   r^2 * log(r) scaling the user wants, but requires a different data structure.

3. FRUSTUM CULLING: Only traverse the octree within the camera's view frustum.
   This reduces the constant factor (from ~4π/3 to ~fov_solid_angle) but
   doesn't change the exponent.

4. CONSOLIDATION: Running Consolidate() marks deep interior/exterior nodes as
   non-mixed, enabling TrulyEmpty/TrulySolid pruning at higher levels. This
   reduces the constant but not the exponent.

5. INCREMENTAL UPDATES: Instead of full traversal, only update nodes that
   changed since last frame (camera movement delta). This amortizes the cost
   but doesn't change worst-case scaling.
""")
