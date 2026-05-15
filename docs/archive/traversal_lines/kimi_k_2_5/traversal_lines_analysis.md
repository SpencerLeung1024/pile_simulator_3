# TraversalLines Scaling Analysis

## Executive Summary

The current octree traversal implementation shows **super-quadratic scaling** with realization radius (~r^2.83) and **unexpected growth** with asteroid size (~R^0.32) even when maintaining constant screen coverage. The algorithm achieves only **0.5-2.6% efficiency**—visiting 40-200× more nodes than necessary.

**Key Finding**: The current implementation scales closer to O(r³) than the expected O(r²·log r), indicating fundamental inefficiencies in how interior volume is being traversed.

---

## Test Methodology

All tests use consistent camera positioning at approximately 2× the asteroid radius, looking at the asteroid surface. Data collected from `dump_debug.txt`.

### Test Configurations

1. **Realization Radius Sweep**: Fixed 10km asteroid, varying realization radius from 10-50m
2. **Asteroid Size Sweep**: Fixed 50m realization radius, varying asteroid size from 20m-10km diameter

---

## Results: Realization Radius Scaling

### Raw Data

| Radius (m) | Octree (ms) | Visited Nodes | Exposed Surface | Efficiency |
|------------|-------------|---------------|-----------------|------------|
| 10 | 16.21 | 359,449 | 9,239 | 2.57% |
| 20 | 124.83 | 2,581,441 | 34,334 | 1.33% |
| 30 | 385.44 | 8,134,729 | 72,357 | 0.89% |
| 40 | 864.43 | 18,396,841 | 125,600 | 0.68% |
| 50 | 1594.98 | 34,511,169 | 186,298 | 0.54% |

### Power Law Fits

| Metric | Observed Power | R² | Expected |
|--------|---------------|-----|----------|
| **Octree Time** | **r^2.77** | 1.000 | r^2 or r^2·log(r) |
| Mesh Time | r^1.93 | 0.999 | r^2 (surface area) |
| **Visited Nodes** | **r^2.83** | 1.000 | r^2·log(r) |
| Exposed Nodes | r^1.84 | 0.999 | r^2 (surface area) |

### Analysis

**The Problem**: Visited nodes scale as r^2.83, significantly worse than the expected r^2·log(r). The efficiency drops from 2.6% at r=10m to 0.5% at r=50m—the algorithm gets *less* efficient as the radius increases.

**Expected Behavior**:
- Surface area scales as r² (correctly reflected in exposed nodes at r^1.84)
- Tree depth to reach surface scales as log₂(r)
- Total should be ~r²·log(r) ≈ r^2.3 for this range

**Observed Behavior**:
- Power of 2.83 suggests volume-like (r³) traversal, not surface
- At r=50m, we visit 34.5M nodes to find 186k exposed (0.5% hit rate)
- The interior "Theta Failed" nodes dominate: 4.3M at r=50m vs 45k at r=10m

---

## Results: Asteroid Size Scaling

### Raw Data

| Asteroid (m) | Octree (ms) | Visited Nodes | Exposed Surface | Efficiency |
|--------------|-------------|---------------|-----------------|------------|
| 20 | 4.08 | 257,801 | 4,308 | 1.67% |
| 100 | 177.99 | 6,720,265 | 49,495 | 0.74% |
| 500 | 659.64 | 16,383,849 | 95,970 | 0.59% |
| 2000 | 1100.80 | 24,761,545 | 138,094 | 0.56% |
| 10000 | 1594.98 | 34,511,169 | 186,298 | 0.54% |

### Power Law Fits

| Metric | Observed Power | Notes |
|--------|---------------|-------|
| Visited Nodes | **R^0.32** | Should be ~constant! |
| Octree Time | Non-linear growth | Time per depth level increases 127× |

### Analysis

**The Problem**: Even with camera distance scaling proportionally with asteroid size (maintaining constant screen coverage), visited nodes grow as R^0.32. Ideally, this should be **constant**—we're looking at the same screen-space area of different-sized asteroids.

**Tree Depth Impact**:
- 20m asteroid: depth ~4.3 levels
- 10km asteroid: depth ~13.3 levels
- Time per depth level: 0.94ms → 120ms (127× increase!)

This indicates that deeper tree traversal is not O(1) per node—cache effects and branch prediction likely degrade with tree depth.

---

## Theoretical Expectations

### What You Expected

> "r² because you need at least r² to represent the surface within the realization radius, and log₂(r) for overhead"

**Expected**: `Visited Nodes = O(r² · log₂(r))`

For r=10m to r=50m:
- Surface area ratio: (50/10)² = 25×
- Log ratio: log₂(50)/log₂(10) ≈ 1.7×
- Expected total: 25 × 1.7 = 42.5×

**Observed**: 34.5M / 359k = 96× (more than 2× worse!)

### What You're Getting

The r^2.83 power suggests the algorithm is visiting:
- All nodes in a sphere of radius r (volume = 4/3 π r³)
- Plus additional overhead for tree traversal

This is **volume-dominated** behavior, not surface-dominated.

---

## Root Cause: Why Scaling is Wrong

### Issue 1: Interior Node Traversal

The "Theta Failed" category represents nodes that are tested for LOD but fail the size/distance test, requiring descent into children. These grow super-linearly:

| Radius | Theta Failed | Growth Rate |
|--------|--------------|-------------|
| 10m | 44,931 | — |
| 20m | 322,680 | 7.2× |
| 30m | 1,016,841 | 3.2× |
| 40m | 2,299,605 | 2.3× |
| 50m | 4,313,896 | 1.9× |

These nodes represent the **volume traversal problem**—we're descending into interior regions that will ultimately be culled.

### Issue 2: No Early Interior Exit

The current algorithm descends into nodes before checking if they're interior (surrounded by solid neighbors). A node at height 5 with 6 solid neighbors should skip traversal entirely—all children are guaranteed interior.

### Issue 3: Theta Test Location

The theta (LOD) test happens at each node, but for an octree:
- Nodes that are "too small for distance" (theta passed) should stop descent
- But we're still visiting all children of those nodes (see Theta Passed → Empty/Solid breakdown)

---

## Recommended Scaling Targets

### Target 1: Realization Radius

**Current**: ~r^2.83
**Target**: r^2 · log₂(r) ≈ r^2.3 for the tested range

**To achieve**:
1. **Frustum culling**: Skip nodes outside view frustum (30-50% reduction)
2. **Interior early-exit**: Store "all neighbors solid" flag at each node, skip descent if set
3. **Deferred neighbor checks**: Only check neighbors for nodes that will actually be rendered

**Expected improvement**: 2-4× reduction in visited nodes at r=50m

### Target 2: Asteroid Size

**Current**: ~R^0.32 (growing)
**Target**: O(1) constant (with fixed screen coverage)

**To achieve**:
1. **Amortized updates**: Don't traverse entire tree every frame
2. **Temporal coherence**: Cache visible nodes, only update near camera movement
3. **LOD for far-field**: Coarser representation for distant octree branches

**Expected improvement**: Flatten curve to near-constant for 100m-10km range

---

## TraversalLines Equation

Based on the data, the current implementation follows approximately:

```
Visited Nodes ≈ 550 × r^2.83 × R^0.32

Where:
  r = realization radius (meters)
  R = asteroid radius (meters)
```

**What it should be**:

```
Visited Nodes ≈ k × r² × log(r/d) × (1 + ε·log(R))

Where:
  d = minimum voxel size (1m)
  ε ≈ 0.1 (small factor for tree depth overhead)
  k = surface density constant (~200-500 depending on view angle)
```

### Simplified Rule of Thumb

For the current implementation:
- **Doubling realization radius** → ~7× more nodes visited (2^2.83)
- **Doubling asteroid size** → ~1.25× more nodes visited (2^0.32)

For the target implementation:
- **Doubling realization radius** → ~5× more nodes visited (4× surface × 1.25× log)
- **Doubling asteroid size** → ~1× more nodes visited (constant)

---

## Conclusion

The current TraversalLines implementation scales as **O(r^2.83)** with realization radius—closer to volume (r³) than surface (r²). This explains why even a 50m realization radius on a 10km asteroid results in 34M node visits.

**The fix requires**:
1. Frustum culling to reduce sphere to visible cone
2. Interior early-exit to avoid descending into hidden volumes
3. Amortized updates to spread cost across frames

With these changes, the scaling should approach the expected **O(r² · log r)** for realization radius and **O(1)** for asteroid size with proportional camera distance.
