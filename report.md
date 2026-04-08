# Asteroid Mining Simulation: Technical Analysis and Pile Simulator 3 Design

## Executive Summary

This report analyzes the challenges of simulating large, diggable asteroids based on two prototype implementations:
- **Rubble Pile Simulator**: Physics-based discrete rocks with n-body gravity (Barnes-Hut)
- **Gridmap Pile Simulator**: Voxel-based asteroid with material layers

The core problem: A 1km diameter asteroid at 1m resolution contains ~500 million voxels. A 10km asteroid contains ~500 billion. Loading this at full fidelity is impossible. The solution requires aggressive level-of-detail (LOD) management, procedural generation, and careful tradeoffs between physics fidelity and scale.

---

## 1. Tradeoffs in Existing Games

### 1.1 Minecraft

**Scale Achievement**: Virtually infinite worlds (60 million blocks in each direction) with full diggability.

**Key Tradeoffs**:

| Feature | Implementation | Cost |
|---------|---------------|------|
| **Chunk System** | 16×16×16 (or 16×16×384) chunks loaded on-demand | Memory bounded by render distance, not world size |
| **Procedural Generation** | Deterministic noise (Perlin/Simplex) + seeds | Zero storage for unmodified terrain |
| **Height Limit** | -64 to +320 blocks | Only ~2% of vertical space is traversable |
| **Block Uniformity** | All blocks are 1m cubes, axis-aligned | No irregular shapes, no rotation |
| **Physics** | Simple AABB collision, no rigid body physics | Blocks don't fall unless explicitly coded (sand/gravel) |
| **Persistence** | Modified chunks saved to disk, unmodified regenerated | Storage proportional to player modifications only |

**Critical Insight**: Minecraft cheats on physics. Most blocks are static decorations. Only entities (mobs, items, falling sand) have real physics. The world is a static collision mesh that changes discretely.

### 1.2 Space Engineers

**Scale Achievement**: 1km+ asteroid bodies with full deformable voxel terrain.

**Key Tradeoffs**:

| Feature | Implementation | Cost |
|---------|---------------|------|
| **Voxel Resolution** | Multiple LODs: 8m, 4m, 2m, 1m, 0.5m | Far areas use 64x fewer voxels |
| **Procedural Asteroids** | Seed-based generation | Asteroids can be regenerated; only modifications stored |
| **Octree Storage** | Sparse voxel octrees | Empty space costs near-zero memory |
| **Physics** | Voxel-based collision hulls generated on-demand | Complex shapes approximate to convex hulls for physics |
| **Streaming** | Asteroids load/unload based on distance | Pop-in visible; max ~100m view distance for detailed voxels |
| **Mining** | Voxels removed in spherical patterns | No debris physics; materials instantly converted to inventory |

**Critical Insight**: Space Engineers separates visual voxels from physics. What you see is high-res; what collides is lower-res. Mining is a visual effect + inventory update, not a physics simulation.

### 1.3 Common Patterns

Both games use:
1. **Procedural generation** - Never store what can be regenerated
2. **Chunk-based LOD** - Distant = simpler
3. **Sparse data structures** - Empty space is free
4. **Separation of concerns** - Visuals ≠ Physics ≠ Gameplay logic

---

## 2. Data Structures for Large Asteroids

### 2.1 The Scale Problem

For a spherical asteroid with diameter D (in meters):
- Volume ≈ 0.52 × D³ voxels at 1m resolution
- A 1km asteroid: ~500M voxels (~500MB raw)
- A 10km asteroid: ~500B voxels (~500GB raw)

Storage requirements scale with the **cube** of diameter. A rubble pile (loose rocks) has similar problems - 10k rocks at 10m diameter implies ~1B rocks at 1m diameter.

### 2.2 Sparse Voxel Octree (SVO)

**Structure**: Hierarchical tree where each node represents an octant of space.

```
Level 0 (Root): 1 node covering entire asteroid (e.g., 10km cube)
Level 1: 8 nodes, each 5km
Level 2: 64 nodes, each 2.5km
...
Level 14: 8^14 nodes, each ~0.6m (for 10km asteroid)
```

**Properties**:
- Empty regions stored as null pointers (sparse)
- Homogeneous regions (solid rock) can collapse to single node with material ID
- LOD naturally falls out of tree depth

**Memory Efficiency**:
- Solid 10km asteroid: ~1 node (entirely homogeneous)
- Hollowed-out asteroid: Only stores modified regions at full depth
- Cave system of 1M voxels: ~1M leaf nodes, but only ~100K internal nodes

### 2.3 Dual Contouring / Adaptively Sampled Distance Fields (ASDF)

For smoother terrain than blocky voxels:
- Store signed distance field (SDF) values at octree corners
- Generate mesh using dual contouring when SDF crosses zero
- Allows smooth caves, overhangs, rounded rocks

**Tradeoff**: Better visuals, but modifications require SDF updates and remeshing.

### 2.4 Chunked Sparse Arrays

**Structure**: Fixed-size chunks (e.g., 32³ voxels) stored in hash map keyed by chunk coordinates.

```
Dict<Vector3I, Chunk> loadedChunks;

class Chunk {
    Material[32,32,32] voxels; // Only allocated if non-empty
    bool isHomogeneous; // Optimization for solid rock chunks
}
```

**Advantages**:
- Fast random access: O(1) chunk lookup + array index
- Easy serialization: One chunk = one file/record
- Godot integration: GridMap and MultiMesh are chunk-friendly

### 2.5 Material Function Representation

Instead of storing every voxel, store a **procedural material function**:

```csharp
Material SampleMaterial(Vector3 worldPos) {
    // Base asteroid composition
    float radiusRatio = worldPos.Length() / asteroidRadius;
    
    // Layered structure (differentiation)
    float iceProb = Mathf.Lerp(0.0f, 1.0f, radiusRatio);
    float metalProb = Mathf.Lerp(1.0f, 0.0f, radiusRatio);
    
    // Add noise for variety
    float noise = PerlinNoise3D(worldPos * 0.1f);
    
    // Determine material
    return DetermineMaterial(iceProb, metalProb, noise);
}
```

**Storage**: Only modifications (mining, player-built structures) are stored. Everything else is regenerated from the function + seed.

**Limitation**: Cannot support arbitrary pre-authored interior structures without an overlay system.

### 2.6 Hybrid Approach (Recommended)

Combine multiple representations by distance from player:

| Distance | Representation | Detail |
|----------|---------------|--------|
| 0-50m | Chunked sparse voxels (0.5m) | Full diggability, physics debris |
| 50-500m | Sparse voxel octree (2m) | Visual LOD, approximate collision |
| 500m-5km | SDF octree (8m) | Impostor rendering, no collision |
| 5km+ | Billboard/impostor | Visual only |

---

## 3. Algorithms for Interior Sensing and Tunneling

### 3.1 Geological Survey System

**Problem**: Player needs to find valuable minerals without digging blindly.

**Solutions**:

1. **Ground-Penetrating Radar (GPR)**
   - Cast rays through SVO, accumulate material density signatures
   - Limited range (100m), consumes power
   - Shows approximate material concentrations

2. **Gravity Mapping**
   - High-density materials (metals) create local gravity anomalies
   - Can be detected from orbit or surface
   - Low resolution but asteroid-wide

3. **Spectroscopic Analysis**
   - Surface materials indicate interior composition
   - Different asteroid types have different differentiation patterns
   - Educational + gameplay hint system

### 3.2 Efficient Tunneling

**Voxel Removal**:
```csharp
void RemoveVoxel(Vector3I pos, float blastRadius) {
    // 1. Determine affected chunks
    var affectedChunks = GetChunksInSphere(pos, blastRadius);
    
    // 2. Update voxel data
    foreach (var chunk in affectedChunks) {
        chunk.RemoveSphere(pos, blastRadius);
        chunk.MarkDirty();
    }
    
    // 3. Spawn debris (nearby only)
    if (blastRadius < 10f) {
        SpawnDebrisParticles(pos, blastRadius);
    }
    
    // 4. Queue mesh rebuild
    meshRebuildQueue.AddRange(affectedChunks);
}
```

**Debris Management**:
- Only spawn physical debris for nearby excavations (< 50m)
- Distant mining: visual effect only, inventory update
- Debris consolidates into "rock piles" after 30 seconds (reduces physics bodies)

### 3.3 Mesh Generation

**Greedy Meshing**: Combine adjacent same-material faces to reduce triangle count by 90%+

**Transvoxel Algorithm**: Smooth transitions between LOD levels to avoid cracks

**Async Meshing**: Use Godot's `ArrayMesh` creation on background threads, swap when ready

---

## 4. Pile Simulator 3 Design

### 4.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    PILE SIMULATOR 3                         │
├─────────────────────────────────────────────────────────────┤
│  Presentation Layer                                         │
│  - MultiMeshInstance3D for rocks (near field)              │
│  - Shader-based impostors (far field)                      │
│  - Voxel mesh chunks (tunnel surfaces)                     │
├─────────────────────────────────────────────────────────────┤
│  Simulation Layer                                           │
│  - Active Physics Zone: RigidBody3D rocks (100m radius)    │
│  - Static Zone: Kinematic bodies (100-500m)                │
│  - Distant Zone: No physics, visual only                   │
├─────────────────────────────────────────────────────────────┤
│  Data Layer                                                 │
│  - Sparse Voxel Octree (material composition)              │
│  - Chunked Modifications (player mining)                   │
│  - Procedural Generator (seed-based regeneration)          │
├─────────────────────────────────────────────────────────────┤
│  Gravity Layer                                              │
│  - Barnes-Hut on demand (when needed for gameplay)         │
│  - Precomputed gravity map for walking                     │
│  - Interior gravity: zero-G tunnels (simplified)           │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 Core Systems

#### 4.2.1 AsteroidManager

```csharp
public partial class AsteroidManager : Node3D {
    // The "infinite" asteroid data
    private SparseVoxelOctree _materialTree;
    private Dictionary<Vector3I, VoxelChunk> _modifiedChunks;
    private ProceduralAsteroidGenerator _generator;
    
    // The player-centric loaded regions
    private Dictionary<Vector3I, PhysicsZone> _activeZones;
    private Dictionary<Vector3I, StaticZone> _staticZones;
    
    // Configuration
    [Export] float _activeZoneRadius = 100f;
    [Export] float _staticZoneRadius = 500f;
    [Export] float _maxAsteroidRadius = 5000f; // 10km diameter
    
    public Material SampleMaterial(Vector3 worldPos) {
        // 1. Check modifications
        Vector3I chunkPos = WorldToChunk(worldPos);
        if (_modifiedChunks.TryGetValue(chunkPos, out var chunk)) {
            var localPos = WorldToLocal(worldPos);
            if (chunk.TryGetMaterial(localPos, out var material)) {
                return material;
            }
        }
        
        // 2. Fall back to procedural
        return _generator.SampleMaterial(worldPos);
    }
}
```

#### 4.2.2 Zone Management

The key insight from Rubble Pile Simulator is that **every rock cannot be a RigidBody3D**. Pile Simulator 3 uses three tiers:

**Active Zone (0-100m from player)**:
- Full physics simulation
- Individual rocks can be dislodged
- Debris from mining has collision
- ~100-500 RigidBody3D maximum

**Static Zone (100-500m)**:
- Visual representation via MultiMeshInstance3D
- Collision as simplified convex hulls or heightfield
- Rocks appear fixed; cannot be individually mined
- ~10,000-50,000 visual instances

**Distant Zone (500m+)**:
- Billboard impostors or shader-based asteroid surface
- No individual rocks visible
- Approximate collision for ship navigation
- Essentially "the surface"

#### 4.2.3 Voxel Chunk System

For tunneling gameplay, use a hybrid:

```csharp
public partial class VoxelChunk : Node3D {
    // The voxel data (only stores modified voxels)
    private Dictionary<Vector3I, Material> _modifications;
    private bool _isSolid; // Optimization: if unmodified, reference generator
    
    // The mesh
    private MeshInstance3D _meshInstance;
    private StaticBody3D _collisionBody;
    
    // State
    private bool _meshDirty = true;
    private float _lastAccessTime;
    
    public void MarkDirty() {
        _meshDirty = true;
        _lastAccessTime = Time.GetTicksMsec();
    }
    
    public void UpdateMeshIfNeeded() {
        if (!_meshDirty) return;
        
        // Generate mesh from voxels
        var mesh = GreedyMeshGenerator.Generate(_modifications);
        _meshInstance.Mesh = mesh;
        _collisionBody.GetChild<CollisionShape3D>().Shape = mesh.CreateTrimeshShape();
        
        _meshDirty = false;
    }
}
```

#### 4.2.4 Procedural Generator with Variation

Expand on Gridmap Pile Simulator's layering:

```csharp
public class ProceduralAsteroidGenerator {
    private FastNoiseLite _baseNoise;
    private FastNoiseLite _detailNoise;
    private ulong _seed;
    
    public Material SampleMaterial(Vector3 worldPos) {
        float radius = worldPos.Length();
        float normalizedRadius = radius / _asteroidRadius;
        
        // Asteroid type determines composition curve
        switch (_asteroidType) {
            case AsteroidType.CType: // Carbonaceous
                return SampleCType(worldPos, normalizedRadius);
            case AsteroidType.SType: // Stony
                return SampleSType(worldPos, normalizedRadius);
            case AsteroidType.MType: // Metallic
                return SampleMType(worldPos, normalizedRadius);
        }
    }
    
    private Material SampleMType(Vector3 pos, float radiusRatio) {
        // Metallic asteroids: iron-nickel core, rocky mantle
        float metalCoreEnd = 0.4f;
        float rockMantleEnd = 0.9f;
        
        // Add noise for irregular boundaries
        float noise = _detailNoise.GetNoise3D(pos.X, posPos.Y, pos.Z) * 0.1f;
        
        if (radiusRatio + noise < metalCoreEnd) {
            // Core: nickel-iron with platinum group metals
            float pgmRichness = _baseNoise.GetNoise3D(pos * 0.01f);
            return pgmRichness > 0.7f ? Material.PGMMetal : Material.IronNickel;
        } else if (radiusRatio + noise < rockMantleEnd) {
            // Mantle: silicates with some metal inclusions
            return Material.Silicate;
        } else {
            // Crust: regolith, possibly ice if far from sun
            return Material.Regolith;
        }
    }
}
```

### 4.3 Gameplay Integration

#### Mining Loop

1. **Scan**: Use GPR to identify material layers (visual overlay)
2. **Drill**: Remove voxels in spherical pattern around drill point
3. **Collect**: Nearby debris auto-collected; distant mining = instant inventory
4. **Process**: Ore processing in player-built facilities

#### Physics Fidelity vs Performance

| Scenario | Physics Level |
|----------|--------------|
| Player walking on surface | Simplified gravity (always toward center) |
| Ship landing/takeoff | Full gravity from AsteroidManager |
| Drilling rock face | RigidBody debris in 20m radius |
| Explosive mining | RigidBody debris in 50m radius, particles beyond |
| Large-scale demolition | Particle effects only, structural collapse via animation |

### 4.4 Memory Budget (Target)

For a 10km asteroid with player having tunneled ~1km of shafts:

| Component | Memory |
|-----------|--------|
| SVO (sparse, mostly procedural) | ~10 MB |
| Modified voxel chunks (1000 chunks @ 32³) | ~50 MB |
| Active physics rocks (500) | ~5 MB |
| Static zone meshes (100 chunks) | ~100 MB |
| Far zone impostors | ~20 MB |
| **Total** | **~185 MB** |

---

## 5. Additional Considerations

### 5.1 Multiplayer Considerations

- **Authoritative server**: Modifications must be server-authoritative
- **Chunk locking**: When player is mining a chunk, lock it to prevent conflicts
- **Delta compression**: Only send modified voxel coordinates, not entire chunks
- **Interest management**: Each player loads zones around themselves

### 5.2 Persistence Strategy

```
Save Format:
- Seed (8 bytes) - regenerates 99.9% of asteroid
- Asteroid type (1 byte)
- Modified chunk count (4 bytes)
- For each modified chunk:
  - Chunk coordinates (12 bytes)
  - Modification count (4 bytes)
  - For each modification:
    - Local position (6 bytes, 16-bit per axis)
    - New material (1 byte)
```

A heavily mined asteroid might have ~100K modifications = ~1MB save file.

### 5.3 Art Pipeline

Instead of unique rocks for every voxel:
- **Material-based meshes**: 5-10 rock meshes per material type
- **Texture atlas**: Single material with UV offsets for variety
- **Shader variation**: Use world position to tint/scale instances
- **Triplanar mapping**: Eliminates UV seams on voxel meshes

### 5.4 Gravity Model Tradeoffs

The original Rubble Pile Simulator used Barnes-Hut for n-body gravity. For gameplay:

**Option A: Simplified Uniform Gravity**
- Always pull toward asteroid center
- Good for walking; inaccurate for orbit

**Option B: Precomputed Gravity Map**
- Sample gravity at grid points, interpolate
- Captures large-scale irregularities
- ~1MB storage for 100m resolution over 10km

**Option C: Barnes-Hut On-Demand**
- Only compute when needed (spacecraft navigation)
- Cache results
- Original implementation can be reused here

**Recommendation**: Option B for walking, Option A for gameplay simplicity, Option C available for advanced orbital mechanics.

### 5.5 Lessons from Prototypes

**From Rubble Pile Simulator**:
- ✅ Barnes-Hut gravity works well for n-body
- ✅ MultiMeshInstance3D is essential for rendering many objects
- ❌ Every rock as RigidBody3D hits physics limits at ~500 objects
- ❌ Collision shape scaling is expensive at load time

**From Gridmap Pile Simulator**:
- ✅ Voxels enable interesting interior variety
- ✅ GridMap is convenient but not scalable to millions
- ❌ Loading all voxels at startup causes multi-minute stalls
- ❌ No physics for individual rocks (purely visual)

**Pile Simulator 3 Solutions**:
1. Tiered physics zones (active/static/distant)
2. Procedural generation + modifications only
3. Async chunk loading/unloading
4. Hybrid voxel (interior) + impostor (exterior) rendering

---

## 6. Recommended Technology Stack (Godot 4)

| System | Godot Feature | Notes |
|--------|--------------|-------|
| Rendering | MultiMeshInstance3D, ShaderMaterials | Impostors for distant rocks |
| Physics | RigidBody3D (limited count), StaticBody3D | Jolt physics, keep active bodies < 500 |
| Voxels | Custom SparseVoxelOctree | Not GridMap for large scale |
| Meshing | ArrayMesh + SurfaceTool | Greedy meshing in C# |
| Threading | Task.Run for mesh generation | Godot 4 thread safety improved |
| Serialization | FileAccess, JSON or custom binary | Delta compression for modifications |
| Noise | FastNoiseLite | Built-in, good performance |

---

## 7. Conclusion

Building a playable asteroid mining game at kilometer scales requires aggressive culling of unnecessary detail. The key strategies are:

1. **Never store what can be regenerated** - Procedural generation with deterministic seeds
2. **Tiered fidelity** - Different representations at different distances
3. **Sparse data structures** - Octrees and hash maps for modifications only
4. **Physics isolation** - Only simulate what the player can currently interact with
5. **Visual smoke and mirrors** - Impostors, billboards, and shader tricks for distant views

Pile Simulator 3 can achieve the goal of tunneling kilometers into a differentiated asteroid by combining the best aspects of both prototypes: the physical interactivity of Rubble Pile Simulator (for nearby rocks) with the scalable interior representation of Gridmap Pile Simulator (via sparse voxels), while avoiding the performance pitfalls of both.

The result should allow a 10km diameter asteroid with rich interior variation, playable at 60 FPS, with only the currently relevant physics being simulated.
