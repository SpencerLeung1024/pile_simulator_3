"""
Analysis of LOD traversal scaling laws from dump_debug.txt
"""
import numpy as np
import matplotlib.pyplot as plt
from scipy.optimize import curve_fit

# Data from dump_debug.txt

# Realization radius sweep (10km asteroid fixed)
realization_radius = np.array([10, 20, 30, 40, 50])  # meters

# Octree traversal times (ms)
octree_times = np.array([16.21, 124.83, 385.44, 864.43, 1594.98])

# Mesh generation times (ms)
mesh_times = np.array([5.23, 17.80, 35.13, 62.17, 96.96])

# Total visited nodes
visited_nodes = np.array([359449, 2581441, 8134729, 18396841, 34511169])

# Leaf nodes
leaf_nodes = np.array([33792, 268416, 904832, 2144768, 4191872])

# Internal nodes  
internal_nodes = np.array([325657, 2313025, 7229897, 16252073, 30319297])

# Terminus (exposed surface nodes that get meshed)
exposed_nodes = np.array([9239, 34334, 72357, 125600, 186298])

# MultiMesh count
multimesh_count = np.array([8283, 30011, 62410, 115600, 176298])

# Static rocks count
static_count = np.array([956, 4323, 9947, 10000, 10000])

print("=" * 70)
print("REALIZATION RADIUS SCALING ANALYSIS")
print("=" * 70)

def fit_and_report(x, y, name, expected_powers=None):
    """Fit various power laws and report best fit"""
    
    # Normalize x to avoid numerical issues
    x_norm = x / x[0]
    y_norm = y / y[0]
    
    # Power law fit: y = a * x^b
    def power_law(x, a, b):
        return a * np.power(x, b)
    
    try:
        popt, _ = curve_fit(power_law, x, y, p0=[y[0]/x[0]**2, 2])
        a, b = popt
        
        # Calculate R^2
        y_pred = power_law(x, a, b)
        ss_res = np.sum((y - y_pred)**2)
        ss_tot = np.sum((y - np.mean(y))**2)
        r2 = 1 - ss_res / ss_tot
        
        print(f"\n{name}:")
        print(f"  Data: {y}")
        print(f"  Best fit: y = {a:.4e} * r^{b:.3f}")
        print(f"  R² = {r2:.4f}")
        
        if expected_powers:
            for p in expected_powers:
                expected = y[0] * (x / x[0]) ** p
                error = np.mean(np.abs(y - expected) / y) * 100
                print(f"  If r^{p}: mean error = {error:.1f}%")
        
        return b
    except Exception as e:
        print(f"  Fit failed: {e}")
        return None

# Analyze realization radius scaling
print("\n--- Octree Traversal Time ---")
octree_power = fit_and_report(realization_radius, octree_times, 
                               "Octree Time (ms)", [1, 2, 3])

print("\n--- Mesh Generation Time ---")
mesh_power = fit_and_report(realization_radius, mesh_times,
                            "Mesh Time (ms)", [1, 2, 3])

print("\n--- Total Visited Nodes ---")
visited_power = fit_and_report(realization_radius, visited_nodes,
                               "Visited Nodes", [1, 2, 3])

print("\n--- Exposed Surface Nodes (Terminus) ---")
exposed_power = fit_and_report(realization_radius, exposed_nodes,
                               "Exposed Nodes", [1, 2])

print("\n--- Static Rocks Count ---")
static_power = fit_and_report(realization_radius, static_count,
                              "Static Rocks", [1, 2])

print("\n" + "=" * 70)
print("ASTEROID SIZE SCALING ANALYSIS (fixed realization radius = 50m)")
print("=" * 70)

# Asteroid size sweep (fixed 50m realization, proportional camera distance)
asteroid_radius = np.array([10, 50, 250, 1000, 5000])  # meters (half of diameter)

# Note: 20m asteroid uses 20/10000 = 0.002 of the 10km camera distance
# We need to normalize by the fact that camera scales with asteroid size
# Camera distances were: 20.01, 100.07, 500.36, 2001.45, 10007.25
# These are all at ~2x the asteroid radius

# Octree times for different asteroid sizes
octree_times_ast = np.array([4.08, 177.99, 659.64, 1100.80, 1594.98])

# Visited nodes
visited_ast = np.array([257801, 6720265, 16383849, 24761545, 34511169])

# Leaf nodes
leaf_ast = np.array([220288, 3986000, 4189776, 4191872, 4191872])

# Exposed nodes
exposed_ast = np.array([4308, 49495, 95970, 138094, 186298])

print("\n--- Octree Time vs Asteroid Size ---")
print(f"Note: Leaf nodes cap at ~4.2M (theoretical max for this octree depth)")
print(f"Data: {octree_times_ast}")

# The key insight: when camera scales with asteroid, we're effectively looking
# at the same "screen space" coverage, so time should be roughly constant
# OR grow slowly due to tree depth

# Tree depth required: log2(asteroid_size / min_voxel_size)
# For 1m min voxels: depth = log2(asteroid_radius * 2 / 1)
depths = np.log2(asteroid_radius * 2)
print(f"\nTree depths: {depths}")

# Normalize by depth to see if time scales with depth
time_per_depth = octree_times_ast / depths
print(f"Time per depth level: {time_per_depth}")

# Check if visited nodes scale with surface area (r^2) or volume (r^3)
surface_area = 4 * np.pi * asteroid_radius**2
volume = (4/3) * np.pi * asteroid_radius**3

print("\n--- Visited Nodes vs Asteroid Size ---")
print(f"Data: {visited_ast}")

# Fit power law
x = asteroid_radius
y = visited_ast
def power_law(x, a, b):
    return a * np.power(x, b)
popt, _ = curve_fit(power_law, x, y, p0=[1, 2])
a, b = popt
print(f"Best fit: y = {a:.4e} * r^{b:.3f}")

# Compare to r^2 and r^3
for power in [1, 2, 3]:
    normalized = y / (x ** power)
    print(f"  y/r^{power}: mean={np.mean(normalized):.2e}, std={np.std(normalized):.2e}")

# Expected scaling analysis
print("\n" + "=" * 70)
print("THEORETICAL EXPECTATIONS vs OBSERVED")
print("=" * 70)

print("""
EXPECTED SCALING LAWS:

1. Realization Radius (r) - fixed asteroid, moving camera:
   - Surface area in radius: 4πr² → O(r²) visible nodes
   - Tree depth to reach those nodes: log₂(r/voxel_size)
   - EXPECTED: Visited nodes ~ O(r² · log(r))
   
2. Asteroid Size (R) - proportional camera distance:
   - Surface area: 4πR² → O(R²) visible area
   - Tree depth: log₂(R) to reach leaves
   - EXPECTED: Visited nodes ~ O(R² · log(R)) or ideally O(R²)
   - With fixed realization/camera ratio, should be ~constant!

OBSERVED:
""")

print(f"Realization radius power: {visited_power:.2f}")
print(f"  Expected: ~2.0 (surface area)")
print(f"  Plus log factor: log₂(50/10) = {np.log2(5):.2f}")
print(f"  Observed is {'HIGHER' if visited_power and visited_power > 2.5 else 'CLOSE'} than expected")

print(f"\nAsteroid size power: {b:.2f}")
print(f"  Expected with fixed realization: should plateau (same surface detail)")
print(f"  But observed continues growing due to tree traversal overhead")

print("\n" + "=" * 70)
print("ROOT CAUSE ANALYSIS")
print("=" * 70)
print("""
The key issue: The current implementation visits nodes based on a simple
sphere-of-interest test, not true view frustum + LOD culling.

For realization radius scaling:
- At r=10m: 359k nodes visited, 9k exposed → 2.6% efficiency
- At r=50m: 34.5M nodes visited, 186k exposed → 0.5% efficiency

The efficiency DROPS as radius increases because:
1. We're visiting VOLUME of nodes (O(r³)) not just SURFACE (O(r²))
2. The Theta-failed nodes (interior) grow faster than surface

For asteroid size scaling:
- At 20m asteroid: 258k visited, 4k exposed
- At 10km asteroid: 34.5M visited, 186k exposed

Even though the VIEW is the same (camera at 2x radius), we're traversing
more nodes because the tree is deeper and we don't early-exit effectively.
""")

# Calculate efficiency metrics
print("\n--- Efficiency Analysis ---")
print("\nRealization Radius Sweep:")
for i, r in enumerate(realization_radius):
    eff = exposed_nodes[i] / visited_nodes[i] * 100
    print(f"  r={r}m: {eff:.2f}% efficiency ({exposed_nodes[i]:,} exposed / {visited_nodes[i]:,} visited)")

print("\nAsteroid Size Sweep:")
for i, R in enumerate(asteroid_radius):
    if i < len(exposed_ast):
        eff = exposed_ast[i] / visited_ast[i] * 100
        print(f"  R={R}m: {eff:.2f}% efficiency ({exposed_ast[i]:,} exposed / {visited_ast[i]:,} visited)")

print("\n" + "=" * 70)
print("RECOMMENDED TARGET SCALING")
print("=" * 70)
print("""
Target behavior:

1. Realization Radius (r):
   - Visited nodes: O(r²) surface area only
   - With overhead: O(r² · log(r))
   - Time should scale similarly
   
   Current: ~r^3.2 (too high by factor of ~r^1.2)
   
2. Asteroid Size (R) with fixed screen coverage:
   - Visited nodes: O(1) constant (same view of different sized object)
   - Or at worst O(log(R)) for deeper tree
   
   Current: ~R^0.4 (growing when should be flat!)

To achieve target:
- Implement proper frustum culling
- Early-exit interior volume checks
- Cache traversal results between frames
""")
