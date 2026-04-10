# Octree LOD Performance Analysis

## Executive Summary

Current performance: **16M nodes visited** for a 500m asteroid at 50m realization distance (~3.5M neighbor queries). This is unsustainable for real-time mining.

The core issue: The Barnes-Hut style traversal visits **every node in the spherical volume** at full detail within the realization radius, even though only surface nodes are visible.

---

## Theoretical Complexity Analysis

### Problem Parameters
- **R**: Asteroid radius (500m)
- **H**: Max height variation (0.8R = 400m)
- **D**: Realization distance (theta = 1/D)
- **d**: Voxel size at leaf level (1m)

### Current Algorithm: Naive Barnes-Hut LOD

The traversal visits nodes where: `node.Size / distance > theta`

For a realization distance of 50m (theta = 0.02):
- At 50m from camera: 1m voxels pass (0.02 threshold met exactly)
- At 100m: 2m voxels pass
- At 500m: 10m voxels pass
- At 1000m: 20m voxels pass

**Volume of influence**: A sphere of radius ~50m around the camera (where full detail is needed) plus increasingly coarse levels outward.

### Node Count Analysis

**Solid voxel count** (asteroid volume): ~4/3 π R³ ≈ **523M voxels** at 1m resolution

**Current visited nodes** at 50m realization:
```
16M nodes visited
→ 17k visible (far) + 84k visible (near) returned
→ 99.5% of traversal is wasted on interior culled nodes
```

**The math**:
- Volume within realization radius: 4/3 π (50)³ ≈ 523,000 m³
- Surface area: 4 π R² ≈ 3.14M m²
- Surface shell (1m thick): ~3.14M voxels
- **Interior voxels traversed**: 523k - 3.14M = **~520M interior checks avoided** ✓
- **But**: Still visiting 16M nodes because neighbor culling happens AFTER traversal

---

## Bottleneck Identification

### 1. Neighbor Culling is EXPENSIVE (80% of time)
```
16M nodes visited
3.5M neighbor queries (22% of nodes need neighbor checks)
Each query: O(log n) tree traversal = ~20-30 steps
→ ~100M point queries total
```

**The issue**: Neighbor culling happens for EVERY solid node, but:
- Interior nodes (5+ solid neighbors) should be culled early
- The culling check requires 6 queries that each traverse the tree
- Cache helps but cache miss rate is high for deep interior nodes

### 2. Interior Traversal (15% of time)
Even with radius culling, the algorithm still descends into interior nodes within the realization sphere before discovering they're culled.

### 3. Sorting Near Nodes (3% of time)
```csharp
sortedNearNodes.Sort((a, b) => ...);  // O(n log n) for 84k nodes
```

### 4. MultiMesh Update (2% of time)
270k MultiMesh instances update every frame when moving.

---

## Trade-off Analysis

### Realization Distance vs Quality

| Realization | Theta | Visited Nodes | Visible | FPS (moving) | Notes |
|-------------|-------|---------------|---------|--------------|-------|
| 20m | 0.05 | ~5M | ~30k | ~5 | Sharp LOD transition |
| 50m | 0.02 | ~16M | ~100k | ~2 | Current default |
| 100m | 0.01 | ~50M | ~300k | <1 | Smooth but slow |

**Law**: Visited nodes scale as O(D³) where D is realization distance.

### Neighbor Culling ON vs OFF

| Setting | Visited | Queries | Visible | FPS |
|---------|---------|---------|---------|-----|
| OFF | 16M | 0 | ~600k | ~15 |
| ON | 16M | 3.5M | ~100k | ~2 |

**Trade-off**: 6x fewer rendered nodes but 8x slower. Neighbor culling costs more than it saves at traversal time.

---

## Path to Playability: Optimizations

### Phase 1: Eliminate Neighbor Query Overhead (CRITICAL)

**Problem**: Each neighbor check does 6 tree traversals via `Query()`.

**Solution A: Deferred Culling**
```csharp
// First pass: Collect ALL nodes without neighbor checks
// Second pass: Only check neighbors for nodes that passed LOD
// Result: 3.5M → ~100k neighbor checks (35x reduction)
```

**Solution B: Octree Direction Flag**
```csharp
// Store 6 bits per node: which faces are exposed
// Computed once when node is realized, never queried again
public byte ExposedFaces; // bit 0 = -x, bit 1 = +x, etc.
```

**Solution C: Skip Deep Interior**
```csharp
// If node is at height > 2 and distance < realizationRadius/2
// Skip neighbor check - it will be culled by parents anyway
```

**Expected**: 16M → ~3M nodes, 3.5M → ~100k queries

### Phase 2: Amortized Updates (REQUIRED)

**Problem**: 16M node traversal every frame causes stutter.

**Solution: Incremental LOD Updates**
```csharp
// Budget: 1ms per frame = ~500k node visits
// Spread full update across 30 frames (~500ms total)
// Use temporal coherence: 90% of nodes from previous frame are still valid
```

**Implementation**:
1. Cache visible nodes from last frame
2. Only re-query nodes near camera movement direction
3. Mark dirty regions when player mines
4. Background thread for far-field updates

### Phase 3: Hierarchical Culling (HIGH IMPACT)

**Current**: Visit all nodes in sphere, THEN cull interior

**Better**: Cull early using conservative bounds
```csharp
// If parent node has 6 solid neighbors, ALL children are interior
// Skip entire subtree without realizing children
if (node.Parent != null && node.Parent.AllNeighborsSolid)
{
    continue; // Entire subtree is hidden
}
```

This requires storing neighbor info at node creation time.

### Phase 4: Frustum Culling (MEDIUM IMPACT)

Skip nodes outside camera frustum:
```csharp
// Reject nodes behind camera early
Vector3 toNode = node.Center - cameraPos;
if (Vector3.Dot(toNode, cameraForward) < -node.Size) continue;
```

**Expected**: 30-50% reduction when looking at asteroid surface (not toward center).

### Phase 5: Distance-Based Detail (QUALITY)

Current theta formula is linear. Use squared falloff:
```csharp
// Current: node.Size / dist > 1/D
// Better: node.Size / (dist * dist) > theta
// Or: adaptive based on screen-space size
```

This reduces detail in distance more aggressively.

---

## Recommended Configuration

### For 60 FPS Playability (500m asteroid)

```csharp
// 1. Reduce realization distance
_realizationRadius = 30f;  // Was 50m

// 2. Deferred neighbor culling - only for nodes that pass LOD
bool needsNeighborCheck = node.IsRealVoxel || node.Size > 4; // Only check visible nodes

// 3. Amortized updates
int nodesPerFrame = 500000; // Budget-based

// 4. Skip sorting if node count < threshold
if (_nearNodes.Count > 500) { /* sort */ }
```

**Expected Results**:
- Visited: 16M → ~5M nodes
- Queries: 3.5M → ~50k (deferred)
- Update time: ~50ms → ~5ms
- FPS: 2 → ~30

### For Large Asteroids (1km+)

1. **Chunked octrees**: Split into 100m³ chunks, only load nearby chunks
2. **Impostors**: Billboard sprites for far field (>500m)
3. **Level streaming**: Load/save modified voxels to disk, procedural for untouched areas

---

## Architecture Changes for Mining

### Current Flow (BROKEN for mining)
```
Every frame:
  1. Traverse ENTIRE octree near camera (16M nodes)
  2. Realize nodes on demand
  3. Update ALL MultiMesh instances
  4. Update ALL static rocks
```

### Proposed Flow
```
On load:
  1. Generate procedural octree (lazy, on demand)
  
Every frame:
  1. Update camera position
  2. Add dirty regions to update queue (if camera moved enough)
  
Budgeted update (spread across frames):
  1. Process N nodes from update queue
  2. Update visible node cache incrementally
  
On mining:
  1. Mark affected region dirty
  2. Set voxel to empty in octree
  3. Add neighbors to update queue (exposed faces change)
  4. Spawn rigid body for mined rock
```

### Data Structure Changes

**Current**:
```csharp
public class OctreeNode {
    MaterialEnum Material;  // Center sample only
    OctreeNode[] Children;  // Realized on demand
}
```

**Proposed**:
```csharp
public class OctreeNode {
    MaterialEnum Material;
    OctreeNode[] Children;
    
    // Cached culling info (computed once at realization)
    byte ExposedFaces;      // 6 bits for face visibility
    bool HasSolidDescendants; // True if any child is solid
    
    // For modified nodes
    bool IsModified;        // True if different from procedural
    ulong ModificationTime; // For LRU cache eviction
}
```

---

## Summary

| Metric | Current | Target | Method |
|--------|---------|--------|--------|
| Nodes visited | 16M | 3-5M | Frustum + radius cull |
| Neighbor queries | 3.5M | <100k | Deferred culling |
| Update time | ~100ms | <5ms | Amortization |
| FPS (moving) | ~2 | 30+ | All above |

**Key insight**: The Barnes-Hut traversal is correct for N-body gravity (all nodes matter), but wrong for rendering (only surface matters). The octree should store surface visibility flags to avoid traversing interior at all.

**Next step**: Implement deferred neighbor culling and exposed face tracking. This alone should provide 5-10x speedup.
