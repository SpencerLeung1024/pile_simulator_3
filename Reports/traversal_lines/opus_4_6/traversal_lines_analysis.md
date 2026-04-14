# TraversalLines Scaling Analysis

## Executive Summary

The Barnes-Hut LOD traversal in [`QueryForLOD()`](DSA/Octree.cs:253) scales as:

$$\text{Visited} \;\approx\; \frac{32\,r^3}{\ln 2}\;\ln\!\Bigl(\frac{2a}{r}\Bigr) \;=\; 32\,r^3\,\log_2\!\Bigl(\frac{2a}{r}\Bigr)$$

where **r** = realization radius and **a** = asteroid radius. The constant 32 ≈ 8·(4/3)π comes from the volume of a sphere (each octree level expands a sphere of r³ nodes) multiplied by the branching factor 8.

**This is r³, not r².** The hoped-for r²·log(r) scaling is impossible with a volumetric octree because the traversal must visit interior nodes to determine they're empty or solid. Only ~0.5% of visited nodes are output-producing surface nodes.

---

## Data Source

All measurements from [`dump_debug.txt`](reports/dump_debug.txt) collected on a 13900H / RTX 4060 laptop.
- Base: 10 km asteroid, 50 m realization radius, neighbor culling on, before consolidation
- Camera on the surface, ~10,007 m from origin

---

## Part 1: Realization Radius Sweep (a = 10,000 m)

| r (m) | Visited | Leaf | ThetaPassed | ThetaFailed | Octree (ms) | MultiMesh |
|------:|--------:|-----:|------------:|------------:|------------:|----------:|
| 10 | 359,449 | 33,792 | 277,246 | 44,931 | 16.21 | 8,283 |
| 20 | 2,581,441 | 268,416 | 1,977,113 | 322,680 | 124.83 | 30,011 |
| 30 | 8,134,729 | 904,832 | 6,184,371 | 1,016,841 | 385.44 | 62,410 |
| 40 | 18,396,841 | 2,144,768 | 13,900,924 | 2,299,605 | 864.43 | 115,600 |
| 50 | 34,511,169 | 4,191,872 | 25,922,372 | 4,313,896 | 1,594.98 | 176,298 |

### Log-log slopes (Visited vs r, all pairs)

Every pair yields a slope between **2.82 and 2.84**, systematically below 3.0:

| Pair | Slope |
|------|------:|
| r=10→20 | 2.844 |
| r=10→50 | 2.836 |
| r=20→50 | 2.830 |
| r=40→50 | 2.819 |

### Power law fits

| Metric | Best fit | R² | Local slopes |
|--------|----------|---:|-------------|
| Visited | 546 · r^2.826 | 0.999999 | 2.84, 2.83, 2.84, 2.82 |
| Leaf | 33.3 · r^3.002 | 1.000000 | 2.99, 3.00, 3.00, 3.00 |
| ThetaPassed | 451 · r^2.802 | 0.999998 | 2.83, 2.81, 2.82, 2.79 |
| ThetaFailed | 68.3 · r^2.826 | 0.999999 | 2.84, 2.83, 2.84, 2.82 |
| Octree ms | 0.032 · r^2.769 | 0.999982 | 2.95, 2.78, 2.81, 2.75 |
| MultiMesh | 70.5 · r^1.999 | 0.999522 | 1.86, 1.81, 2.14, 1.89 |

**Key observations:**
- **Leaf** scales as exactly **r³** — the number of leaf-resolution cells in a sphere of radius r
- **Visited** scales as ~r^2.83, pulled below 3.0 by the log₂(2a/r) factor slowly varying with r
- **MultiMesh** (output) scales as ~**r²** — the surface area within the realization sphere
- **ThetaFailed is exactly 12.5% of Visited** in every case (see [Part 3](#part-3-structural-identity))

### Why the slope is 2.83 not 3.0

A pure r³ law gives slope = 3.0. The model Visited = C·r³·log₂(2a/r) has:

$$\frac{d\ln(\text{Visited})}{d\ln r} = 3 + \frac{d\ln\log_2(2a/r)}{d\ln r} = 3 - \frac{1}{\ln(2a/r)}$$

At a = 10,000 and r = 30: slope = 3 − 1/ln(667) = 3 − 0.154 = **2.85** ✓

Testing the model Visited/(r³·log₂(2a/r)):

| r | Visited/(r³·log₂(2a/r)) |
|--:|------------------------:|
| 10 | 32.78 |
| 20 | 32.38 |
| 30 | 32.12 |
| 40 | 32.06 |
| 50 | 31.94 |

This ratio is constant to within **2.6%** — the model fits.

---

## Part 2: Asteroid Size Sweep (r = 50 m)

| a (m) | H | Visited | Leaf | ThetaPassed | ThetaFailed | Octree (ms) |
|------:|--:|--------:|-----:|------------:|------------:|------------:|
| 20 | 6 | 257,801 | 220,288 | 0 | 32,225 | 4.08 |
| 100 | 8 | 6,720,265 | 3,986,000 | 1,828,899 | 840,033 | 177.99 |
| 500 | 10 | 16,383,849 | 4,189,776 | 10,071,746 | 2,047,981 | 659.64 |
| 2,000 | 12 | 24,761,545 | 4,191,872 | 17,400,134 | 3,095,193 | 1,100.80 |
| 10,000 | 15 | 34,511,169 | 4,191,872 | 25,922,372 | 4,313,896 | 1,594.98 |

H = octree height = ⌈log₂(2a)⌉

### Log-log slopes (Visited vs a)

The slopes are **not constant** because the relationship is logarithmic, not a power law:

| Pair | Slope |
|------|------:|
| a=20→100 | 2.03 |
| a=100→500 | 0.55 |
| a=500→2000 | 0.30 |
| a=2000→10000 | 0.21 |

This is consistent with Visited ~ log(a) for fixed r.

### Testing the model

| a | log₂(2a/r) | Predicted | Actual | Error |
|------:|--------:|---------:|-------:|------:|
| 20 | 0.8* | N/A | 257,801 | degenerate |
| 100 | 2.00 | 8,000,000 | 6,720,265 | +19% |
| 500 | 4.32 | 17,287,712 | 16,383,849 | +5.5% |
| 2,000 | 6.32 | 25,287,712 | 24,761,545 | +2.1% |
| 10,000 | 8.64 | 34,575,425 | 34,511,169 | +0.2% |

*a=20 is degenerate: the asteroid fits entirely within the realization radius, so the formula doesn't apply.

The model converges as a/r grows. At a=100 (a/r=2), boundary effects cause ~19% error. At a=10000 (a/r=200), the error is negligible.

### Leaf count saturation

Leaf count saturates at **~4,191,872** for a ≥ 500. This is because at r=50, exactly (4/3)π·50³ ≈ 523,599 cells are in the expansion sphere, but the actual count is higher because the theta boundary isn't a perfect sphere — it's per-node distance, creating a jagged boundary. Regardless, once a >> r, the leaf count is independent of a (the leaves only see the local volume around the camera).

---

## Part 3: Structural Identity

A critical finding: **Visited = 8 · ThetaFailed + 1**, exactly, for every data point:

| r | ThetaFailed × 8 + 1 | Visited | Match |
|--:|--------------------:|--------:|:-----:|
| 10 | 359,449 | 359,449 | ✓ |
| 20 | 2,581,441 | 2,581,441 | ✓ |
| 30 | 8,134,729 | 8,134,729 | ✓ |
| 40 | 18,396,841 | 18,396,841 | ✓ |
| 50 | 34,511,169 | 34,511,169 | ✓ |

This is a **tautological identity of octree traversal**: every visited node is either the root (1) or one of 8 children of an expanded node (ThetaFailed). The corollary is that ThetaFailed is always Visited/8 = **12.5%** of all visited nodes.

### Component breakdown (r sweep)

| r | Leaf | ThetaPassed | ThetaFailed | TrulyEmpty | TrulySolid |
|--:|-----:|------------:|------------:|-----------:|-----------:|
| 10 | 9.4% | 77.1% | 12.5% | 1.0% | 0.0% |
| 20 | 10.4% | 76.6% | 12.5% | 0.5% | 0.0% |
| 30 | 11.1% | 76.0% | 12.5% | 0.3% | 0.0% |
| 40 | 11.7% | 75.6% | 12.5% | 0.3% | 0.0% |
| 50 | 12.1% | 75.1% | 12.5% | 0.2% | 0.0% |

**ThetaPassed dominates** (~75% of all visited nodes). These are internal nodes far enough that they pass the theta criterion and are treated as LOD approximations. They scale as r^2.80, slightly below Visited because their proportion decreases as r grows (more nodes reach the leaf level).

---

## Part 4: Per-Node Cost

| r | Octree (ms) | Visited | ns/node |
|--:|------------:|--------:|--------:|
| 10 | 16.21 | 359,449 | 45.1 |
| 20 | 124.83 | 2,581,441 | 48.4 |
| 30 | 385.44 | 8,134,729 | 47.4 |
| 40 | 864.43 | 18,396,841 | 47.0 |
| 50 | 1,594.98 | 34,511,169 | 46.2 |

Per-node cost is **~46–48 ns** for the r sweep (constant, as expected).

| a | Octree (ms) | Visited | ns/node |
|------:|------------:|--------:|--------:|
| 20 | 4.08 | 257,801 | 15.8 |
| 100 | 177.99 | 6,720,265 | 26.5 |
| 500 | 659.64 | 16,383,849 | 40.3 |
| 2,000 | 1,100.80 | 24,761,545 | 44.5 |
| 10,000 | 1,594.98 | 34,511,169 | 46.2 |

The a sweep shows **increasing ns/node** from 16 → 46 as the asteroid grows. This is caused by deeper [`GetExposedFaces()`](DSA/Octree.cs:217) neighbor queries — each calls [`Query()`](DSA/Octree.cs:189) which traverses root-to-leaf, and deeper trees (larger a → larger H) mean longer queries. A secondary factor is cache pressure: larger trees have more widely-scattered memory accesses.

---

## Part 5: Neighbor Culling Analysis

| Metric | With Culling | Without | Impact |
|--------|:-----------:|:-------:|:------:|
| Octree (ms) | 1,594.98 | 1,508.59 | +5.7% overhead |
| Meshes (ms) | 96.96 | 6,763.21 | **98.6% savings** |
| MultiMesh count | 176,298 | 12,862,295 | **98.6% reduction** |
| FPS | 144 | 10 | **14.4× improvement** |

Neighbor culling via [`GetExposedFaces()`](DSA/Octree.cs:217) adds only 86 ms to the octree traversal but saves **6,666 ms** in mesh processing by eliminating 12.7 million interior nodes from the multimesh. Net saving: **6,580 ms per frame**.

---

## Part 6: Why r³ Not r²

The user's hope was for r²·log₂(r), reasoning that the surface within the realization radius is 2D (area = r²). This is correct for the **output** (MultiMesh ≈ r²), but wrong for the **traversal cost**.

### The output is r², but the work is r³

The key insight is the difference between **what you produce** and **what you visit**:

```
Output nodes (surface):  ~r²    ← the asteroid surface is 2D
Visited nodes (volume):  ~r³    ← the octree traverses volume
```

### Why the traversal visits volume

1. **The theta criterion is volumetric.** At each height h, `nodeTheta = 2^h / distance`. Nodes within distance `2^h · r` get expanded. This is a **sphere**, not a shell.

2. **Empty nodes must be visited to be identified as empty.** At the leaf level (h=0), the expansion sphere of radius r contains ~(4/3)πr³ cells. Roughly half are above the surface (empty) and half below (solid). Both are visited. The empty ones cost traversal time but produce no output.

3. **Each level contributes r³ nodes.** At height h, the expansion radius is `2^h·r` and the cell size is `2^h`. The number of cells in the expansion sphere is (4/3)π(2^h·r)³/(2^h)³ = (4/3)π·r³. This is **independent of h** — every level costs the same.

4. **The number of levels is log₂(2a/r).** Levels continue until the expansion sphere (2^h·r) exceeds the asteroid diameter (2a). This gives h_max = log₂(2a/r) levels.

5. **Total: (4/3)π · r³ · log₂(2a/r) per level × 8 children per expanded node ≈ 32 · r³ · log₂(2a/r).**

### Geometric intuition

Imagine standing on the asteroid surface with the realization radius r. The octree doesn't have a "surface detector" — it must examine the full cubic volume within r to determine which cells are on the surface vs interior vs exterior. The volumetric cost is inherent to the octree data structure.

### The theoretical constant

C_theory = 8 · (4/3)π ≈ **33.51**

C_empirical ≈ **32.26**

The ~3.7% difference is due to `TrulyEmpty` and `TrulySolid` pruning — nodes entirely outside or inside the asteroid that get skipped. TrulyEmpty scales as ~r² (a surface-area effect: it prunes the outer shell of the expansion sphere), which is why it only reduces the constant, not the exponent.

---

## Part 7: Complete Scaling Law

### For the traversal (Visited nodes and Octree time):

$$\boxed{\text{Visited} \approx 32\,r^3\,\log_2\!\Bigl(\frac{2a}{r}\Bigr)}$$

$$\text{Octree time} \approx 46\,\text{ns} \;\times\; \text{Visited}$$

Valid when a >> r. Degenerate when a ≤ r (entire asteroid fits within realization sphere).

### For the output (visible nodes):

$$\text{MultiMesh} \approx 70\,r^2 \quad\text{(surface area)}$$

### For the frame time:

$$\text{Frame time} \approx 46\,\text{ns}\;\times\;32\,r^3\,\log_2(2a/r) + \text{mesh time}$$

With neighbor culling, mesh time ≈ 0.55 μs × MultiMesh count.

### Practical implications at base parameters (a=10km, r=50m):

| Component | Count | Time |
|-----------|------:|-----:|
| Visited nodes | 34.5M | 1,595 ms |
| MultiMesh instances | 176K | 97 ms |
| **Total** | | **1,692 ms** |

This is far over the 16.67 ms budget for 60 FPS, dominated by the r³·log(a/r) traversal.

---

## Part 8: Paths to r² Scaling

To achieve the desired r²·log(r), the traversal must avoid visiting interior volume. Options:

1. **Surface octree**: Only store nodes within a fixed depth of the surface (e.g., within MaxHeight of the terrain envelope). Interior nodes are implicitly solid. This changes the data structure from a full-volume octree to a surface shell, giving r² leaves × log(a/r) internal ≈ **r²·log(a/r)**.

2. **Dual pruning**: Pre-mark the `Mixed` flag on all internal nodes via a full realization pass. Then `TrulySolid` pruning can skip the entire interior at every level, reducing each level's contribution from r³ to ~r² (the surface shell at that resolution). This is what [`Consolidate()`](DSA/Octree.cs:406) partially does, but it requires full realization first.

3. **Lazy realization with boundary detection**: During traversal, if a node at height h is entirely inside `MinRadius`, immediately mark it `TrulySolid` and skip. If entirely outside `MaxRadius`, mark `TrulyEmpty`. The current code does this in [`RealizeChildren()`](DSA/Octree.cs:154) but only for children — the parent must still be visited and expanded to check each child.

4. **Sparse octree / hashmap**: Only store surface-adjacent nodes. Query by spatial hash instead of tree traversal. Eliminates the tree overhead entirely, giving O(surface nodes) = O(r²) per query.
