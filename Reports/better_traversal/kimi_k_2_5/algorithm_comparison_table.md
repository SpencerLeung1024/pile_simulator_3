# Algorithm Comparison Quick Reference

## Summary Table

| Algorithm | Complexity | At r=50m | Pros | Cons | Best For |
|-----------|-----------|----------|------|------|----------|
| **Current** | O(r³ × log(a/r)) | 34.5M nodes | Simple, correct | Too slow, 0.5% efficiency | None—needs replacement |
| **SSFF** | O(r²) | ~200K nodes | Surface-only, natural LOD | Disjoint surface issues | Dynamic worlds, caves |
| **SSH** | O(r²) | ~200K nodes | O(1) access, no traversal | High memory, needs pre-gen | Static/slowly-changing |
| **TPHC** | O((r/2ʰ)² + r²) | ~50K + 150K | Fast coarse filter | Shell maintenance overhead | Large static asteroids |
| **DFST** | O(pixels × log(a)) | ~1M beams | Screen-space scaling | Needs GPU, overdraw | High-end GPUs |
| **TSC** | O(r²) → O(1) | ~20K new | 90% cache hit | First frame still slow | Camera often still |
| **Chunk Mesh** | O(r²) | ~500 chunks | Industry standard | Memory for cache | Most games |

---

## Decision Flowchart

```
Is your world mostly static?
│
├─ YES → Use Chunk Meshing (Minecraft-style)
│        • Pre-generate meshes
│        • O(r²) with O(1) per frame
│        • Industry proven
│
└─ NO → Is camera often stationary?
        │
        ├─ YES → Use TSC + SSFF
        │        • Cache between frames
        │        • Near O(1) for small movements
        │
        └─ NO → Do you have GPU compute?
                │
                ├─ YES → Use DFST
                │        • Pixel-perfect
                │        • Independent of r
                │
                └─ NO → Use TPHC + SSFF
                         • Pre-computed shell
                         • Surface-only traversal
```

---

## Implementation Difficulty

| Algorithm | Code Complexity | Risk | Time to Implement |
|-----------|----------------|------|-------------------|
| Current | ✓ Simple | — | Done |
| Chunk Mesh | ✓✓ Moderate | Low | 2-3 days |
| SSFF | ✓✓ Moderate | Medium | 1-2 days |
| TPHC | ✓✓✓ Complex | Low | 3-4 days |
| TSC | ✓✓ Moderate | Medium | 1-2 days |
| SSH | ✓✓✓ Complex | High | 4-5 days |
| DFST | ✓✓✓✓ Very Complex | High | 1-2 weeks |

---

## Expected Performance Gains

### Current Baseline (r=50m, a=10km)
- Visited nodes: 34,511,169
- Octree time: 1,595 ms
- FPS: ~0.6

### After Chunk Meshing
- Visited nodes: N/A (cached)
- Mesh generation: ~100ms (one-time per chunk)
- Render time: ~5-10 ms
- FPS: 60-120

### After SSFF
- Visited nodes: ~200,000
- Octree time: ~10 ms
- FPS: 60+

### After TPHC + SSFF
- Visited nodes: ~50,000
- Octree time: ~2-3 ms
- FPS: 144+

---

## Hybrid Recommendations by Use Case

### Case 1: Static Asteroid (Your Step 1)
**Recommended**: TPHC + Chunk Meshing

```csharp
// Pre-compute surface shell at height 4
// Generate chunk meshes for shell nodes
// Cache chunks
// Per frame: render cached chunks
```

**Expected**: 60+ FPS, 20× speedup

---

### Case 2: Player Modifications (Your Step 2)
**Recommended**: SSFF + TSC

```csharp
// Use SSFF for dynamic surface finding
// Cache visible nodes
// On modification: invalidate affected nodes
// Re-flood-fill from modified region
```

**Expected**: 30-60 FPS during edits

---

### Case 3: Rigid Rocks + Physics (Your Step 3)
**Recommended**: Keep Octree + Chunk Mesh Overlay

```csharp
// Octree: physics, connectivity, modifications
// Chunks: rendering only
// Sync on modification
```

**Expected**: Best of both worlds

---

## Key Trade-offs

### Memory vs Speed
| Approach | Memory | Speed |
|----------|--------|-------|
| Current (no cache) | Low | Very Slow |
| SSFF | Low | Fast |
| Chunk Mesh | Medium (cached meshes) | Very Fast |
| SSH | High (all surface) | Fastest |

### Static vs Dynamic
| Approach | Static World | Dynamic World |
|----------|--------------|---------------|
| Chunk Mesh | ✓ Perfect | ✗ Needs remesh |
| SSFF | ✓ Good | ✓ Good |
| TPHC | ✓ Perfect | △ Shell update cost |

### Implementation Risk
| Approach | Risk Level | Mitigation |
|----------|-----------|------------|
| Chunk Mesh | Low | Well-documented, many examples |
| SSFF | Medium | Test with simple scenes first |
| TPHC | Low | Add to existing system gradually |
| SSH | High | Prototype with small world first |
| DFST | High | Start with CPU version |

---

## My Top 3 Recommendations

### 1. Immediate Win: Chunk-Based Meshing
**Why**: Industry standard, well-understood, biggest bang for buck
**Effort**: 2-3 days
**Gain**: 20-50× speedup

### 2. Follow-up: SSFF for Dynamic Regions
**Why**: Handles modifications without full remesh
**Effort**: 1-2 days
**Gain**: Handles Step 2 and 3 cleanly

### 3. Polish: Temporal Cache
**Why**: Makes camera movement nearly free
**Effort**: 1 day
**Gain**: Another 5-10× for common case

---

## Quick Start Code Skeleton

### Chunk-Based (Recommended First)

```csharp
public class VoxelChunk
{
    public const int Size = 16;
    public Vector3I Position; // Chunk coordinates
    public Mesh Mesh; // Generated mesh
    public bool IsDirty; // Needs remesh
    
    public void GenerateMesh(Octree octree)
    {
        // Surface-only mesh generation
        // Cache result
    }
}

public class ChunkManager
{
    private Dictionary<Vector3I, VoxelChunk> _chunks = new();
    
    public void Update(Vector3 cameraPos)
    {
        // Load chunks near camera
        // Unload distant chunks
        // Remesh dirty chunks
    }
    
    public void Render(Frustum frustum)
    {
        foreach (var chunk in _chunks.Values)
        {
            if (frustum.Intersects(chunk.Bounds))
                chunk.Mesh.Draw();
        }
    }
}
```

### SSFF (Recommended Second)

```csharp
public List<OctreeNode> SurfaceQuery(Vector3 cameraPos, float theta)
{
    var result = new List<OctreeNode>();
    var queue = new Queue<OctreeNode>();
    var visited = new HashSet<OctreeNode>();
    
    // Find seeds
    var seeds = FindSurfaceSeeds(cameraPos);
    foreach (var seed in seeds)
    {
        queue.Enqueue(seed);
        visited.Add(seed);
    }
    
    // Flood fill
    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        
        if (ShouldUseNode(node, theta, cameraPos))
        {
            result.Add(node);
        }
        else
        {
            foreach (var child in GetSurfaceChildren(node))
            {
                if (!visited.Contains(child))
                {
                    visited.Add(child);
                    queue.Enqueue(child);
                }
            }
        }
    }
    
    return result;
}
```
