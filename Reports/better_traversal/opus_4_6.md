# Better Traversal Algorithms for Surface-Based Octree Rendering

**Author**: Claude Opus 4.6  
**Context**: Pile Simulator 3 — asteroid octree with Barnes-Hut LOD  
**Problem**: [`QueryForLOD()`](DSA/Octree.cs:253) scales as r³·log₂(a/r). We want r²·log₂(a/r) or better.

---

## Why Top-Down Can Never Be r²

The current algorithm starts at the root and descends recursively. Every internal node that fails the theta test is expanded into 8 children. The identity `Visited = 8 · ThetaFailed + 1` (proven in [traversal_lines_analysis.md](reports/traversal_lines/opus_4_6/traversal_lines_analysis.md:130)) means the total work is dictated by _how many nodes get expanded_.

Within the realization radius r, **every** internal node gets expanded because `node.Size / distance > theta` is always true when `distance < r`. The number of internal nodes in a sphere of radius r is:

```
Σ (height h=0 to H) (4/3)π(r/2^h)³ ≈ (4/3)πr³ · Σ 8^(-h) ≈ (32/7)πr³
```

The `TrulyEmpty` and `TrulySolid` early exits help, but only for nodes that are **entirely** one material. The surface shell (where `Mixed = true`) has volume proportional to r² × shell_thickness. For your asteroid with `MaxHeight = 0.8R`, the mixed shell is thick — most of the sphere of radius r around a surface camera is mixed. So you traverse ~r³ nodes regardless.

**The fundamental issue**: top-down traversal _discovers_ the surface by exhaustively eliminating the volume. Surface-based traversal starts _on_ the surface and never leaves it.

---

## Algorithm 1: Seed-and-Flood Surface Traversal

This is the algorithm you sketched in [`.clinerules`](.clinerules). Formalized:

### Core Idea

1. **Seed**: Drill from root to a known surface voxel at the desired LOD level (the camera position, or the closest surface point). This is O(log₂(a)) — one root-to-leaf path.
2. **Flood**: BFS/DFS from the seed. For each visited node, find its 6 face neighbors. Add neighbors to the queue if they are also surface nodes (Mixed, or solid with at least one empty face neighbor).
3. **Stop**: When all queued nodes are farther than the realization radius, or all surface nodes within range have been visited.

### Multi-Resolution Extension

The realization radius defines a single LOD level (height 0 = 1m voxels). For the far field, you need coarser nodes. The approach:

1. **Fine pass** (height 0): Flood-fill surface nodes within distance r from the camera. Output = static rocks.
2. **Coarse passes** (height 1, 2, ...): For each LOD level h, flood-fill surface nodes of size 2^h in the annular ring from r·2^(h-1) to r·2^h. Output = multimesh instances.
3. Each pass produces ~r² nodes (the surface area within that annular ring). There are ~log₂(a/r) passes. Total: **r² · log₂(a/r)**.

Alternatively, do a single flood that dynamically adjusts LOD level based on distance:

```
Queue: [(seed_node, desired_height)]
For each (node, target_h):
    if node.Height > target_h:
        expand node, enqueue Mixed children at same target_h
    elif node.Height == target_h:
        output node
        for each face neighbor at height target_h:
            compute neighbor's desired LOD from distance to camera
            enqueue (neighbor, desired_LOD_height)
```

### Neighbor Finding: The Critical Subroutine

The make-or-break detail is finding neighbors efficiently. Three options:

#### Option A: Root-to-leaf query (current approach)
```csharp
OctreeNode neighbor = Query(node.Center + direction * node.Size, node.Height);
```
Cost: O(log₂(a)) per neighbor = O(H) where H is tree height. This is what [`GetExposedFaces()`](DSA/Octree.cs:217) already does. For a flood of S surface nodes, total cost is 6·S·H.

At r=50, a=10000: S ≈ 4πr² ≈ 31,400, H ≈ 15, so 6·31,400·15 ≈ **2.8M** operations. Compare to the current 34.5M visited nodes. That's a **12× improvement** just from algorithmic change, and it gets better as r grows (r² vs r³).

#### Option B: Samet's octree neighbor algorithm (1989)
Given a node, find its face neighbor by:
1. Walk **up** to the nearest common ancestor (the first ancestor where the neighbor is in a different octant)
2. Walk **down** to the corresponding child

Cost: O(H) worst case but O(1) amortized for sequential neighbors. Requires parent pointers.

```csharp
public class OctreeNode {
    public OctreeNode Parent;  // NEW: back-pointer
    // ...
}

public OctreeNode GetFaceNeighbor(OctreeNode node, int face) {
    // face: 0=-x, 1=-y, 2=-z, 3=+x, 4=+y, 5=+z
    // Mirror the appropriate octant bit to find neighbor octant
    int axis = face % 3;       // 0=x, 1=y, 2=z
    int bit = 1 << axis;       // octant bit for this axis
    bool positive = face >= 3; // +x/+y/+z?
    
    // Walk up until we find an ancestor where we can step sideways
    OctreeNode current = node;
    Stack<int> path = new();
    
    while (current.Parent != null) {
        int octant = current.Parent.GetOctant(current.Center);
        bool onBoundary = positive ? ((octant & bit) != 0) : ((octant & bit) == 0);
        
        if (!onBoundary) {
            // Neighbor is a sibling — flip the bit and walk down
            int neighborOctant = octant ^ bit;
            OctreeNode neighbor = current.Parent.Children[neighborOctant];
            // Walk back down using stored path
            while (path.Count > 0 && neighbor.Children != null) {
                int childOctant = path.Pop() ^ bit; // Mirror across axis
                neighbor = neighbor.Children[childOctant];
            }
            return neighbor;
        }
        
        path.Push(current.Parent.GetOctant(current.Center));
        current = current.Parent;
    }
    
    return null; // No neighbor (edge of octree)
}
```

This is faster than Option A when the neighbor is nearby in the tree (common case for surface traversal). But it requires parent pointers, which you don't currently store.

#### Option C: Hash map of node positions
Store all realized nodes in a `Dictionary<(Vector3I, int), OctreeNode>` keyed by (position, height). Neighbor lookup is O(1).

```csharp
Dictionary<(Vector3I pos, int height), OctreeNode> nodeMap;

OctreeNode GetNeighbor(OctreeNode node, Vector3I direction) {
    Vector3I neighborPos = ToVector3I(node.Center) + direction * node.Size;
    return nodeMap.GetValueOrDefault((neighborPos, node.Height));
}
```

Simple, fast, but uses more memory. Good for a first implementation.

### Handling the Disjoint Problem

You identified a real concern: if the asteroid is split into disconnected pieces (e.g., a player mines a slice through it), flood fill from one seed won't reach the other piece.

Solutions:
1. **Multiple seeds**: After flood finishes, check if there are unexplored directions. Pick a new seed by querying along camera sight lines or at regular angular intervals.
2. **Coarse-to-fine**: First flood at a coarse level (height 5 = 32m blocks). At this scale, disjoint pieces are likely still connected or at least detectable. Then refine.
3. **Keep a registry of disconnected bodies**: When a mining operation separates a piece, detect it (connected component analysis on the affected region) and register it as a separate body with its own seed.
4. **Hybrid**: Use top-down for coarse LOD (where r³ at coarse resolution is cheap) and surface flood for fine LOD (where the volume savings matter most).

### Complexity Analysis

| Component | Cost | Notes |
|-----------|------|-------|
| Seed drill | O(H) = O(log₂(a)) | One root-to-leaf path |
| Surface flood | O(S) | S = surface nodes in range ≈ r² |
| Neighbor queries | O(S · H) or O(S) | Depends on method (A, B, or C) |
| **Total** | **O(r² · log₂(a))** | With method A |
| **Total** | **O(r²)** amortized | With method B or C |

Compare to current: O(r³ · log₂(a/r)).

---

## Algorithm 2: Dual-Grid Surface Tracking

Used by Hermite data dual contouring and some SVO implementations.

### Core Idea

Instead of tracking individual voxels, track **surface-crossing edges**. An edge crosses the surface if one endpoint is solid and the other is empty. The set of all surface-crossing edges defines the surface implicitly.

For an octree:
1. For each leaf node on the surface, store which of its 12 edges cross the surface.
2. When traversing, only visit nodes that contain surface-crossing edges.
3. This set is inherently 2D (the surface), so it scales as r².

### Application to Your System

You already compute something similar with [`ExposedFaces`](DSA/Octree.cs:46). A node with `ExposedFaces != 0x00` has at least one face bordering empty space — it's a surface node. The dual-grid idea extends this:

```csharp
// Mark edges, not just faces
// An edge is a pair of adjacent nodes that differ in material (one empty, one solid)
// Traversal only follows surface edges
public struct SurfaceEdge {
    public Vector3I Position;
    public int Axis; // 0=x, 1=y, 2=z
    public int Height; // LOD level
}

HashSet<SurfaceEdge> surfaceEdges;
```

The surface edge set can be maintained incrementally: when a voxel is mined, update only the ~6 edges touching it. No global re-traversal needed.

### Why This Helps

The key advantage is **incremental updates**. The current system re-traverses the entire octree every time the camera moves. With a surface edge set:
- Camera moves → check which surface edges enter/leave the realization radius. Cost: O(Δr · r) where Δr is the camera movement distance.
- Mining → update ~6 edges. Cost: O(1).

---

## Algorithm 3: Chunked Surface Lists (The Minecraft Approach)

### Core Idea

Divide space into fixed-size chunks (e.g., 16³ or 32³ voxels). Each chunk maintains:
1. A flat array of voxels (or a mini-octree for compression).
2. A **surface list**: the subset of voxels that are solid and have at least one empty neighbor.
3. A **visibility flag**: is this chunk near the surface of the asteroid? (Entirely solid or entirely empty chunks are skipped.)

### Why It Works for Minecraft/Space Engineers

- Chunks are the unit of loading/saving. Only nearby chunks are in memory.
- Surface lists are precomputed when a chunk is generated or modified. Rendering iterates only surface lists.
- The number of non-empty, non-solid chunks is proportional to **surface area** — the surface of a sphere intersects O(r²/c²) chunks of side length c.

### Application to Your System

```
Chunk size: 32m (height 5 octree nodes)
Asteroid radius 500m → (1000/32)³ ≈ 30,000 chunks total
Surface chunks (within ~MaxHeight of surface): ~4π(500²)/(32²) · (0.8·500/32) ≈ 38,000
Interior/exterior chunks: skipped entirely
```

Each surface chunk contains at most 32³ = 32,768 voxels, of which ~32² ≈ 1,024 are surface voxels (one layer). Total surface voxels across all chunks: 38,000 × 1,024 ≈ **39M** — but you only process chunks near the camera.

Within realization radius r = 50: ~(100/32)³ ≈ 30 chunks, each with ~1,024 surface voxels = **30,720 voxels**. This agrees with your MultiMesh count of ~30k at r=50.

### Hybrid: Octree for LOD + Chunks for Surface

Keep the octree for far-field LOD (coarse nodes at height 3+). Use chunks only within the realization radius. This gives:
- Far field: O(r² / c²) chunks × O(1) per chunk = O(r²) (just check which chunks are surface chunks)
- Near field: O(number_of_surface_chunks × surface_voxels_per_chunk) = O(r²)
- Total: O(r²)

---

## Algorithm 4: Persistent Surface Set with Incremental LOD Updates

### Core Idea

Don't recompute the visible set from scratch every frame. Maintain it persistently and update incrementally.

### Data Structure

```csharp
class SurfaceCache {
    // Active surface nodes, keyed by position and LOD level
    Dictionary<(Vector3I, int), OctreeNode> activeSurface;
    
    // Frontier: surface nodes at the edge of the active set
    // When camera moves, expand/contract the frontier
    SortedSet<(float distance, OctreeNode node)> frontier;
    
    Vector3 lastCameraPos;
}
```

### Update Algorithm

When the camera moves by Δ:

1. **Expand frontier**: Nodes that were outside the realization radius but are now inside. For each, drill down to leaf level and check if it's surface. Cost: O(Δ · r) nodes to check (a strip of width Δ around the realization sphere).

2. **Contract frontier**: Nodes that were inside but are now outside. Collapse them to their parent LOD level. Cost: O(Δ · r).

3. **Re-LOD far field**: Nodes whose apparent angular size changed enough to warrant LOD change. Typically few per frame. Cost: O(Δ · r · log₂(a/r)).

**Total per frame**: O(Δ · r · log₂(a/r)) instead of O(r² · log₂(a/r)).

For a camera moving at 10 m/s at 60 FPS, Δ ≈ 0.17m per frame. With r=50:
- Per frame: 0.17 × 50 × 15 ≈ **127 node operations**
- Compare to current: 34,500,000 per frame

This is a **270,000× reduction** in per-frame work at the cost of a one-time O(r²) initialization.

### Mining Updates

When a voxel is mined:
1. Remove it from `activeSurface`.
2. Check its 6 neighbors. Any that were interior and are now exposed: add to `activeSurface`.
3. Cost: O(1) per mined voxel (plus O(H) for neighbor queries).

This is ideal for gameplay — mining is a local operation that shouldn't trigger global re-traversal.

---

## Algorithm 5: Sphere-Traced Surface Discovery

### Core Idea

Cast rays from the camera outward (e.g., on a fibonacci sphere or a screen-space grid) and find where each ray hits the asteroid surface. This discovers surface nodes in O(ray_count · log₂(a)) time.

### Application

```
Ray count: r² (one per square meter of surface at distance r)
Per ray: O(log₂(a)) to drill from root to the hit voxel
Total: O(r² · log₂(a))
```

This naturally gives you the visible set (frustum-culled!) and is trivially parallelizable on the GPU.

### Downsides

- Misses surface nodes behind other surface nodes (occlusion). Fine for rendering but bad if you need the full surface set.
- Misses thin features between rays. Can be mitigated with adaptive ray density.
- Not great for mining updates (doesn't maintain a persistent set).

---

## Comparison

| Algorithm | Traversal | Per Frame (moving) | Per Frame (still) | Mining Update | Complexity | Implementation |
|-----------|-----------|-------------------|-------------------|---------------|------------|----------------|
| **Current (top-down)** | Volume | O(r³·log(a/r)) | O(r³·log(a/r)) | Retraverse all | Simple | Already done |
| **1: Seed+Flood** | Surface | O(r²·log(a)) | O(r²·log(a)) | Retraverse all | Moderate | Need neighbor finding |
| **2: Dual-Grid Surface** | Surface | O(r²) | O(1) | O(1) | Complex | Significant refactor |
| **3: Chunked** | Surface | O(r²/c²) | O(1) | O(1) per chunk | Moderate | Different data structure |
| **4: Persistent+Incremental** | Surface | O(Δ·r·log(a/r)) | O(1) | O(1) | Complex | Need careful bookkeeping |
| **5: Sphere Trace** | Visible surface | O(r²·log(a)) | O(1) cached | Recast affected rays | Simple | GPU-friendly |

---

## My Recommendation

### Phase 1: Seed-and-Flood with Hash Map Neighbors (Algorithm 1 + Option C)

This is the smallest change that gets you from r³ to r². Implementation sketch:

```csharp
public List<OctreeNode> QuerySurface(Vector3 cameraPos, float theta, bool neighborCulling)
{
    var result = new List<OctreeNode>();
    var visited = new HashSet<Vector3I>();  // (center * 1000) as integer key
    var queue = new Queue<OctreeNode>();
    
    // Step 1: Find seed — drill to surface at camera position
    OctreeNode seed = FindSurfaceNear(cameraPos);
    if (seed == null) return result;
    
    queue.Enqueue(seed);
    visited.Add(ToKey(seed));
    
    // Step 2: Flood fill along surface
    while (queue.Count > 0)
    {
        OctreeNode node = queue.Dequeue();
        
        // Determine desired LOD for this node's distance
        float dist = cameraPos.DistanceTo(node.Center);
        int desiredHeight = DesiredLODHeight(dist, theta);
        
        if (node.Height > desiredHeight && node.Mixed)
        {
            // Need finer detail — expand and enqueue surface children
            if (node.Children == null) RealizeChildren(node);
            foreach (var child in node.Children)
            {
                if (child.IsTrulyEmpty) continue;
                Vector3I key = ToKey(child);
                if (visited.Add(key))
                    queue.Enqueue(child);
            }
        }
        else
        {
            // This node is at the right LOD level — output it
            if (node.Material != MaterialEnum.Empty)
            {
                if (!neighborCulling || GetExposedFaces(node) != 0x00)
                    result.Add(node);
            }
            
            // Enqueue face neighbors at the same LOD level
            for (int face = 0; face < 6; face++)
            {
                OctreeNode neighbor = GetFaceNeighborAtHeight(node, face, node.Height);
                if (neighbor == null || neighbor.IsTrulyEmpty) continue;
                Vector3I key = ToKey(neighbor);
                if (visited.Add(key))
                    queue.Enqueue(neighbor);
            }
        }
    }
    
    return result;
}

private int DesiredLODHeight(float distance, float theta)
{
    // theta = size / distance → size = theta * distance
    // height where size = 2^h → h = log2(theta * distance)
    return Math.Max(0, (int)Math.Floor(Math.Log2(theta * distance)));
}

private OctreeNode FindSurfaceNear(Vector3 pos)
{
    // Drill down to find the surface node closest to the camera
    // Use the generator to find the surface point along the radial direction
    Vector3 dir = pos.Normalized();
    float surfaceDist = Generator.Radius + Generator.GetHeight(pos);
    Vector3 surfacePoint = dir * surfaceDist;
    return Query(surfacePoint, 0, stopOnTrulyEmpty: true, stopOnTrulySolid: false);
}
```

**Expected complexity**: O(r² · H) where H = log₂(a). At r=50, a=10000: ~31,400 × 15 = **471,000 operations** vs current **34,500,000**. That's a **73× reduction**.

### Phase 2: Add Persistence (Algorithm 4)

Once seed-and-flood works, wrap it in a persistent cache. The flood becomes the initialization, and subsequent frames only update the boundary. This gets per-frame cost down to O(Δ · r · H), which at 60 FPS is ~2,000 operations per frame.

### Phase 3: Chunks for Mining (Algorithm 3)

When you add player modifications, switch the near-field to chunks. Each chunk maintains its own surface list. Mining updates are local to one chunk. The octree remains as the backing store for procedural generation and far-field LOD.

---

## The Neighbor-Finding Problem in Detail

Since every surface algorithm depends on efficient neighbor finding, this deserves extra attention.

### Your Current [`GetExposedFaces()`](DSA/Octree.cs:217) Approach

Queries 6 points, each doing a root-to-leaf traversal at the neighbor's height. The problem: each `Query()` call traverses independent paths from the root, making it O(6H) per node. For a flood of S nodes, total neighbor work is O(6SH).

But notice: **consecutive flood nodes are spatially adjacent**. Their query paths share most of their ancestors. A smarter approach exploits this.

### Approach: Path Caching

Maintain a cache of recently traversed paths. When querying a neighbor, check if the path overlaps with a cached path and start from the deepest cached ancestor:

```csharp
// LRU cache of recent query paths
Dictionary<Vector3I, OctreeNode> pathCache;  // position → deepest known ancestor

OctreeNode QueryWithCache(Vector3 point, int stopHeight)
{
    Vector3I key = ToKey(point, stopHeight);
    if (pathCache.TryGetValue(key, out OctreeNode cached))
        return cached;
    
    // Find the deepest cached ancestor
    OctreeNode start = Root;
    for (int h = Root.Height; h > stopHeight; h--)
    {
        Vector3I ancestorKey = ToKey(point, h);
        if (pathCache.TryGetValue(ancestorKey, out OctreeNode ancestor))
        {
            start = ancestor;
            break;
        }
    }
    
    // Traverse from the deepest cached point
    OctreeNode result = QueryFrom(start, point, stopHeight);
    pathCache[key] = result;
    return result;
}
```

This reduces the amortized cost per neighbor query from O(H) to O(1)–O(3) for flood-fill patterns where consecutive queries are nearby.

### Approach: Parent Pointers (Samet)

Add `OctreeNode Parent` to every node. Neighbor finding becomes a local tree walk (up to common ancestor, then back down). This is the theoretically optimal approach but requires a structural change to [`OctreeNode`](DSA/Octree.cs:7).

### Approach: Morton Code Indexing

Assign each node a Morton code (Z-order curve index). Neighbor finding is arithmetic:

```csharp
// Morton code: interleave bits of x, y, z
// Node at position (3, 5, 2) at height 0:
//   x=011, y=101, z=010 → morton = 010_110_011 (read bit-triples)
// +x neighbor: increment x component → (4, 5, 2) → new morton code
// This is O(1) arithmetic + O(1) hash lookup

ulong MortonNeighbor(ulong morton, int axis, bool positive)
{
    // Increment/decrement the component along the given axis
    // Using the standard morton add/subtract operations
    // (well-known bit manipulation, see references)
}
```

Morton codes also give spatial locality for free — nodes close in space are close in morton order, which is cache-friendly for BFS traversal.

---

## Addressing Specific Concerns from `.clinerules`

### "26 neighbors and if they are also exposed, adds them to be explored"

You only need **6 face neighbors** for a watertight surface flood, not 26. Edge and corner neighbors (26-connectivity) would over-connect the surface and could jump across thin gaps. Face-connectivity (6) matches how [`ExposedFaces`](DSA/Octree.cs:46) already works — each bit corresponds to one face.

If you want diagonal connectivity for visual smoothness, use **18-connectivity** (6 faces + 12 edges, skip 8 corners). This prevents diagonal "staircase" gaps while avoiding the corner-jumping issue.

### "There's a problem if the asteroid is disjoint"

Yes, but this is handled naturally:

1. **Before player modifications**: The asteroid is contiguous by construction (it's a deformed sphere). One seed reaches everything.
2. **After a slice-through**: The mining operation knows which voxels were destroyed. When a cut separates two regions, the voxels on each side of the cut are now surface voxels. Each region's surface is reachable from any of its own surface voxels. You just need **one seed per connected component**.
3. **Detection**: When flood fill from the main seed terminates, check if the total surface area matches expectations (e.g., previous frame's count). If significantly less, there's an orphaned component. Find it by casting rays or checking known voxel positions.
4. **Gravity**: When separation is detected, the orphaned chunk becomes a rigid body anyway (per your Step 3 plan). It gets its own seed and its own surface cache.

### "There might be a better algorithm used in practice that I don't know about"

The algorithms above are what's used in practice:
- **Minecraft**: Chunks (Algorithm 3). 16³ chunks, each with a precomputed mesh. Only surface blocks are meshed. Chunk updates are local.
- **Space Engineers**: Chunks + octree hybrid. Large voxel terrain uses clipmaps (concentric rings of detail around the camera, similar to Algorithm 4).
- **Dual Universe**: Continuous LOD octree with surface extraction (Dual Contouring variant, Algorithm 2).
- **No Man's Sky**: Marching cubes on a signed distance field, computed per-chunk. GPU-accelerated.
- **Astroneer**: Surface-tracked deformable terrain with chunk-based updates.

The common thread: **nobody does top-down full-octree traversal for rendering**. They all either track the surface explicitly (flood/chunks) or extract it from a scalar field (marching cubes/dual contouring).

---

## What About Mesh Generation?

Once you have surface nodes efficiently, the next bottleneck is mesh generation. Your current approach (one cube per voxel via MultiMesh) works but produces blocky visuals. Future options:

- **Greedy meshing**: Merge adjacent same-material faces into larger quads. Reduces triangle count by ~90%. Works perfectly with the surface set.
- **Marching cubes**: Generate smooth triangulated meshes from the density field. Higher quality but more complex. Operates per-chunk.
- **Dual contouring**: Like marching cubes but preserves sharp features. Used in Dual Universe.

All of these operate on the surface set, not the volume. So getting the surface set efficiently (this report) is the prerequisite.

---

## Implementation Priority

1. **Seed-and-flood with hash map** — gets you r² scaling with minimal refactoring (~200 lines changed)
2. **Persistent cache with incremental updates** — gets per-frame cost near zero for steady-state camera movement (~300 more lines)
3. **Chunk system for mining** — needed for Step 2 (player modifications) anyway

The first item alone should take your frame times from ~1600ms to ~20ms at r=50, a=10000. The second should make it run at the monitor's refresh rate regardless of r.
