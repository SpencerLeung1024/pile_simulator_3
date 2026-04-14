# Better Surface-Wise Traversal Algorithms

## Problem Summary

The current [`QueryForLOD()`](DSA/Octree.cs:253) uses a **top-down volumetric traversal** that scales as **O(r³ × log(a/r))**. For a realization radius `r` on an asteroid of radius `a`, it visits ~32r³ log₂(2a/r) nodes to find ~70r² surface nodes—only **0.5% efficiency** at r=50m.

The fundamental issue: **To find surface, we traverse volume.** The octree has no "surface detector"—it must descend through interior nodes to find surface leaves.

---

## Key Insight: Surface vs Volume

| Metric | Current | Desired |
|--------|---------|---------|
| Visited nodes | O(r³) | O(r²) |
| Scaling exponent | 2.83 | ~2.0 |
| At r=50m | 34.5M nodes | ~200K nodes |
| Efficiency | 0.5% | ~90%+ |

The surface of a sphere has r² cells. A traversal that visits only exposed surface nodes should visit **~r² nodes**, not r³.

---

## Algorithm 1: Surface-Seeded Flood Fill (SSFF)

The user's intuition about seed-based surface exploration is correct. Here's a refined version:

### Core Idea
Instead of descending from root, **find surface seeds** and **flood-fill along the surface**.

### Algorithm
```csharp
public List<OctreeNode> QuerySurfaceLOD(Vector3 cameraPos, float theta)
{
    var result = new List<OctreeNode>();
    var visited = new HashSet<OctreeNode>();
    var queue = new Queue<OctreeNode>();
    
    // Phase 1: Find initial surface seeds
    // Drill down from root along camera direction to find visible surface
    var seeds = FindSurfaceSeeds(cameraPos, theta);
    
    foreach (var seed in seeds)
    {
        queue.Enqueue(seed);
        visited.Add(seed);
    }
    
    // Phase 2: Flood-fill along surface
    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        
        // Apply LOD test
        float nodeTheta = node.Size / cameraPos.DistanceTo(node.Center);
        if (nodeTheta < theta)
        {
            // Far enough to use this node as-is
            if (IsExposed(node))
                result.Add(node);
            continue;
        }
        
        // Too close—need children
        if (node.Children == null)
            RealizeChildren(node);
        
        // Only enqueue children that are on the surface
        foreach (var child in node.Children)
        {
            if (!visited.Contains(child) && IsSurfaceNode(child))
            {
                visited.Add(child);
                queue.Enqueue(child);
            }
        }
    }
    
    return result;
}
```

### Surface Detection
```csharp
bool IsSurfaceNode(OctreeNode node)
{
    // A node is on the surface if:
    // 1. It's not fully empty (has material or children with material)
    // 2. At least one face is exposed to empty space
    
    if (node.IsTrulyEmpty) return false;
    
    // Quick check: if truly solid but not exposed, skip
    if (node.IsTrulySolid)
        return GetExposedFaces(node) != 0;
    
    // Mixed: may have surface children
    return true;
}
```

### Seed Finding
```csharp
List<OctreeNode> FindSurfaceSeeds(Vector3 cameraPos, float theta)
{
    var seeds = new List<OctreeNode>();
    
    // Cast rays in a cone around camera forward
    // For each ray, find the surface intersection
    var rayDirections = GenerateConeRays(cameraPos, fov: 90, count: 64);
    
    foreach (var dir in rayDirections)
    {
        // Ray-octree intersection
        var hit = RaycastOctree(cameraPos, dir, maxDistance: realizationRadius);
        if (hit != null && hit.node.Material != MaterialEnum.Empty)
        {
            // Walk up to find appropriate LOD level
            var lodNode = GetLODNode(hit.node, theta, cameraPos);
            seeds.Add(lodNode);
        }
    }
    
    return seeds.Distinct().ToList();
}
```

### Complexity Analysis
- **Seed finding**: O(k × log(a)) for k rays (typically 64-256)
- **Flood fill**: Each surface node checks 6 neighbors → O(surface_nodes × 6) = O(r²)
- **Total**: O(r² + k log(a)) ≈ **O(r²)**

### Advantages
- Only visits surface nodes
- Natural LOD handling (larger nodes for distant surface)
- No wasted work on interior volume

### Challenges
- **Disjoint surfaces**: If player digs a tunnel through the asteroid, seeds on one side won't reach the other. **Solution**: Multiple seed passes from different directions, or maintain a "surface cache" from previous frames.
- **Narrow features**: Thin spikes might be missed by coarse seed rays. **Solution**: Adaptive seed density based on previous frame's surface complexity.

---

## Algorithm 2: Sparse Surface Hash (SSH)

Store **only surface voxels** in a spatial hash, not the full octree.

### Data Structure
```csharp
public class SparseSurfaceOctree
{
    // Only stores nodes that are on the surface
    // Key: (center, height) → Value: node data
    private Dictionary<(Vector3, int), SurfaceNode> _surfaceNodes;
    
    // Implicit interior: anything not in hash is solid
    // Implicit exterior: anything outside asteroid radius is empty
}

public struct SurfaceNode
{
    public Vector3 Center;
    public int Height;
    public MaterialEnum Material;
    public byte ExposedFaces; // Which faces touch empty space
    // No children pointer—LOD is handled by height
}
```

### Query
```csharp
public List<SurfaceNode> QuerySurface(Vector3 cameraPos, float theta)
{
    var result = new List<SurfaceNode>();
    
    // Determine required LOD level at this distance
    int targetHeight = CalculateLODHeight(cameraPos.DistanceTo(asteroidCenter), theta);
    
    // Iterate spatial hash cells within view frustum
    foreach (var cell in GetFrustumCells(cameraPos, targetHeight))
    {
        if (_surfaceNodes.TryGetValue(cell, out var node))
        {
            // Check if this LOD level is appropriate
            if (node.Height <= targetHeight + 1 && node.Height >= targetHeight - 1)
            {
                result.Add(node);
            }
        }
    }
    
    return result;
}
```

### Complexity Analysis
- **Storage**: O(surface area × surface_depth) = O(r² × constant)
- **Query**: O(visible_surface_cells) = O(r²)
- **No tree traversal**: Direct hash lookup

### Advantages
- O(1) access to any surface node
- No traversal overhead
- Easy parallelization

### Challenges
- **Memory**: Need to store all surface nodes upfront
- **Modification**: Player edits require updating surface hash (local operation)
- **Generation**: Requires initial pass to find all surface nodes

---

## Algorithm 3: Two-Phase Hierarchical Culling (TPHC)

Combine coarse surface detection with fine refinement.

### Phase 1: Coarse Surface Shell
Pre-compute a low-resolution "surface shell" octree at height 3-4 (8-16m cells). Only store:
- Shell nodes (have both solid and empty neighbors)
- Nothing else

```csharp
public class SurfaceShell
{
    // Only height 3+ nodes that are on the surface
    public Dictionary<Vector3I, ShellNode> Shell;
}

public struct ShellNode
{
    public Vector3I GridPos;
    public int Height; // 3, 4, 5, etc.
    public bool HasSolidChild; // Has solid descendants
    public bool HasEmptyChild; // Has empty descendants
}
```

### Phase 2: Fine Traversal
```csharp
public List<OctreeNode> QueryTwoPhase(Vector3 cameraPos, float theta)
{
    var result = new List<OctreeNode>();
    
    // Phase 1: Find shell nodes within realization radius
    var shellNodes = _surfaceShell.QuerySphere(cameraPos, realizationRadius);
    
    // Phase 2: Descend only into shell nodes
    foreach (var shellNode in shellNodes)
    {
        // Check LOD
        float nodeTheta = (1 << shellNode.Height) / cameraPos.DistanceTo(shellNode.Center);
        
        if (nodeTheta < theta)
        {
            // Use shell node as-is
            result.Add(GetRepresentativeNode(shellNode));
        }
        else
        {
            // Descend into full octree, but only within this shell node's bounds
            var localResult = DescendShellNode(shellNode, cameraPos, theta);
            result.AddRange(localResult);
        }
    }
    
    return result;
}
```

### Complexity Analysis
- **Shell query**: O((r/2^h)²) for shell height h
- **Fine descent**: Only within shell nodes near camera
- **Total**: O((r/2^h)² + r² × (fine_fraction)) ≈ **O(r²)**

### Advantages
- Reduces search space dramatically
- Shell is small: for 10km asteroid at height 4 (16m), shell has ~4π(10000/16)² ≈ 50K nodes
- Interior volume completely skipped

### Challenges
- Shell must be maintained during edits
- Extra memory for shell structure

---

## Algorithm 4: Depth-First Surface Tracing (DFST)

Inspired by GPU ray marching—trace "beams" from camera and only realize nodes along the beam.

### Core Idea
Instead of visiting all nodes in a sphere, **trace from camera to surface** and only realize nodes along that path.

### Algorithm
```csharp
public List<OctreeNode> QueryBeamLOD(Vector3 cameraPos, float theta)
{
    var result = new List<OctreeNode>();
    var beams = GenerateCameraBeams(cameraPos, fov, resolution);
    
    foreach (var beam in beams)
    {
        // March along beam, realizing nodes as needed
        var hit = BeamMarchOctree(beam, theta, result);
    }
    
    return result;
}

OctreeNode BeamMarchOctree(Ray beam, float theta, List<OctreeNode> outNodes)
{
    float t = 0;
    OctreeNode current = Root;
    
    while (t < maxDistance)
    {
        // Find next octree cell intersection
        var (entry, exit, node) = RayOctreeIntersection(beam, t, current);
        
        if (node == null) break;
        
        // Check LOD
        float nodeTheta = node.Size / beam.Origin.DistanceTo(node.Center);
        
        if (nodeTheta < theta && node.Material != MaterialEnum.Empty)
        {
            // Add to output and stop this beam
            outNodes.Add(node);
            break;
        }
        
        if (node.Children == null && !node.IsRealVoxel)
            RealizeChildren(node);
        
        // Continue through children
        t = exit;
    }
    
    return null;
}
```

### Complexity Analysis
- **Beams**: N beams (e.g., 1280×720 for pixel-perfect)
- **Per beam**: O(log(a) + log(r)) steps (octree depth + refinement)
- **Total**: O(N × log(a)) ≈ **O(screen_pixels × log(a))**

This is **independent of r**! The work depends on screen resolution, not realization radius.

### Advantages
- Work scales with screen pixels, not world volume
- Naturally handles LOD (farther beams hit larger nodes)
- Very cache-friendly (sequential beam processing)

### Challenges
- **Overdraw**: Adjacent beams may realize the same nodes
- **Gaps**: Need sufficient beam density to not miss thin features
- **GPU-friendly**: Best implemented as compute shader

---

## Algorithm 5: Temporal Surface Cache (TSC)

Cache visible surface nodes from previous frame, only update near camera movement.

### Data Structure
```csharp
public class SurfaceCache
{
    // Nodes from previous frame
    public Dictionary<Vector3I, CachedNode> CachedNodes;
    
    // Camera position when cached
    public Vector3 CachedCameraPos;
    
    // Invalidate radius around camera movement
    public float InvalidationRadius;
}

public struct CachedNode
{
    public OctreeNode Node;
    public float DistanceToCamera;
    public bool IsVisible;
}
```

### Update Algorithm
```csharp
public List<OctreeNode> QueryWithCache(Vector3 cameraPos, float theta)
{
    float moveDist = cameraPos.DistanceTo(_cache.CachedCameraPos);
    
    if (moveDist < _cameraMoveThreshold)
    {
        // Camera barely moved—reuse cache
        return GetCachedVisibleNodes();
    }
    
    // Camera moved significantly
    // 1. Invalidate nodes near the movement path
    InvalidateNodesAlongPath(_cache.CachedCameraPos, cameraPos);
    
    // 2. Query new region around current camera (using any algorithm above)
    var newNodes = QuerySurfaceLOD(cameraPos, theta);
    
    // 3. Merge with valid cached nodes
    UpdateCache(newNodes);
    
    return GetCachedVisibleNodes();
}
```

### Complexity Analysis
- **Stationary camera**: O(1)
- **Moving camera**: O(new_surface_area) = O(r² × movement_fraction)
- **Amortized**: Near O(1) for small movements

---

## Recommended Approach: Hybrid SSFF + TPHC + TSC

For your use case, I recommend combining three algorithms:

### Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                    Temporal Surface Cache                    │
│         (Reuse 90%+ of nodes between frames)                │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Two-Phase Hierarchical Culling                  │
│    Phase 1: Query pre-computed surface shell (height 4)     │
│    Phase 2: Descend only into visible shell nodes           │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Surface-Seeded Flood Fill (SSFF)                │
│    Find seeds from shell, flood-fill to adjacent surface     │
│    Only visits exposed surface nodes                         │
└─────────────────────────────────────────────────────────────┘
```

### Expected Performance
| Component | Complexity | At r=50m |
|-----------|-----------|----------|
| Surface Shell Query | O((r/16)²) | ~3K nodes |
| SSFF from seeds | O(r²) | ~200K nodes |
| Cache hit | O(1) | ~90% reuse |
| **Total visited** | **O(r²)** | **~20K new + 180K cached** |
| **vs Current** | **r³ → r²** | **~20× faster** |

---

## Implementation Roadmap

### Phase 1: Surface Shell (Immediate)
1. Generate surface shell during octree construction
2. Store shell in separate spatial structure
3. Modify `QueryForLOD` to only descend into shell nodes

**Expected gain**: 5-10× reduction in visited nodes

### Phase 2: SSFF (Next)
1. Implement raycast-based seed finding
2. Replace top-down stack with surface queue
3. Add neighbor-based surface expansion

**Expected gain**: Another 2-5× reduction

### Phase 3: Temporal Cache (Final)
1. Cache visible nodes between frames
2. Invalidate on camera movement
3. Incremental updates

**Expected gain**: Near-constant time for small movements

---

## Why These Work

| Algorithm | Key Insight |
|-----------|-------------|
| **SSFF** | Surface is connected—find one point, walk to neighbors |
| **SSH** | Don't store interior; it's implicit |
| **TPHC** | Pre-filter to surface shell at coarse level |
| **DFST** | Work = screen pixels, not world volume |
| **TSC** | Camera moves slowly; reuse yesterday's work |

The current algorithm visits **r³** because it treats the octree as a spatial index. These algorithms treat it as a **surface representation**—which is what you actually want to render.
