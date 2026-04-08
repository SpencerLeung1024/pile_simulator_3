using Godot;
using System;
using System.Collections.Generic;

namespace DSA
{
    /// <summary>
    /// Represents a single node in the sparse voxel octree.
    /// </summary>
    public class OctreeNode
    {
        public bool IsLeaf { get; set; }
        public MaterialEnum? Material { get; set; } // null = empty, non-null = has material
        public OctreeNode[] Children { get; set; } // 8 children if not leaf
        public Vector3 Center { get; set; }
        public float Size { get; set; }
        public int Depth { get; set; } // Depth level in the octree (0 = root)

        /// <summary>
        /// Creates a new leaf node.
        /// </summary>
        public OctreeNode(Vector3 center, float size, int depth, MaterialEnum? material)
        {
            Center = center;
            Size = size;
            Depth = depth;
            Material = material;
            IsLeaf = true;
        }

        /// <summary>
        /// Creates a new internal node with children.
        /// </summary>
        public OctreeNode(Vector3 center, float size, int depth, OctreeNode[] children)
        {
            Center = center;
            Size = size;
            Depth = depth;
            Children = children;
            IsLeaf = false;
            Material = null;
        }

        /// <summary>
        /// Gets the bounds of this node as an Axis-Aligned Bounding Box.
        /// </summary>
        public Aabb GetBounds()
        {
            Vector3 halfSize = new Vector3(Size / 2, Size / 2, Size / 2);
            return new Aabb(Center - halfSize, halfSize * 2);
        }

        /// <summary>
        /// Returns true if this node intersects or is inside the given AABB.
        /// </summary>
        public bool Intersects(Aabb bounds)
        {
            return GetBounds().Intersects(bounds);
        }

        /// <summary>
        /// Gets the 8 child centers for subdivision.
        /// </summary>
        public Vector3[] GetChildCenters()
        {
            float childSize = Size / 2;
            float offset = childSize / 2;

            return new Vector3[]
            {
                new Vector3(Center.X - offset, Center.Y - offset, Center.Z - offset), // 0: -x, -y, -z
                new Vector3(Center.X + offset, Center.Y - offset, Center.Z - offset), // 1: +x, -y, -z
                new Vector3(Center.X - offset, Center.Y + offset, Center.Z - offset), // 2: -x, +y, -z
                new Vector3(Center.X + offset, Center.Y + offset, Center.Z - offset), // 3: +x, +y, -z
                new Vector3(Center.X - offset, Center.Y - offset, Center.Z + offset), // 4: -x, -y, +z
                new Vector3(Center.X + offset, Center.Y - offset, Center.Z + offset), // 5: +x, -y, +z
                new Vector3(Center.X - offset, Center.Y + offset, Center.Z + offset), // 6: -x, +y, +z
                new Vector3(Center.X + offset, Center.Y + offset, Center.Z + offset)  // 7: +x, +y, +z
            };
        }
    }

    /// <summary>
    /// Sparse voxel octree for representing the pristine asteroid.
    /// Provides O(log n) sampling and LOD-based queries.
    /// </summary>
    public class VoxelOctree
    {
        private OctreeNode _root;
        private float _asteroidRadius;
        private ulong _seed;
        private int _maxDepth;
        private bool _enableCrossSectionCut;
        private ProceduralAsteroid _generator;

        /// <summary>
        /// The maximum depth of the octree. Higher = finer detail.
        /// </summary>
        public int MaxDepth => _maxDepth;

        /// <summary>
        /// The radius of the asteroid.
        /// </summary>
        public float AsteroidRadius => _asteroidRadius;

        /// <summary>
        /// The seed used for procedural generation.
        /// </summary>
        public ulong Seed => _seed;

        /// <summary>
        /// If true, cuts the asteroid at z=0 to show cross-section.
        /// </summary>
        public bool EnableCrossSectionCut
        {
            get => _enableCrossSectionCut;
            set => _enableCrossSectionCut = value;
        }

        /// <summary>
        /// Creates a new voxel octree with procedural generation.
        /// </summary>
        /// <param name="seed">Random seed for deterministic generation</param>
        /// <param name="radius">Asteroid radius in meters</param>
        /// <param name="maxDepth">Maximum octree depth (default 8)</param>
        /// <param name="enableCrossSectionCut">Enable z=0 cross-section cut</param>
        public VoxelOctree(ulong seed, float radius, int maxDepth = 8, bool enableCrossSectionCut = false)
        {
            _seed = seed;
            _asteroidRadius = radius;
            _maxDepth = maxDepth;
            _enableCrossSectionCut = enableCrossSectionCut;
            _generator = new ProceduralAsteroid(seed, radius);

            // Calculate root size to encompass the entire asteroid
            // Root size = diameter * some margin to ensure asteroid fits
            float rootSize = radius * 2.5f;

            // Build the octree starting from root
            _root = BuildNode(Vector3.Zero, rootSize, 0);
        }

        /// <summary>
        /// Recursively builds an octree node.
        /// </summary>
        private OctreeNode BuildNode(Vector3 center, float size, int depth)
        {
            // Check if this node is completely outside the asteroid
            if (!NodeIntersectsAsteroid(center, size))
            {
                // Empty space - return null (won't be added to parent)
                return null;
            }

            // Check if this node is fully inside the asteroid and small enough
            if (depth >= _maxDepth || NodeIsFullyInsideAsteroid(center, size))
            {
                // Create leaf node with material
                MaterialEnum? material = DetermineMaterial(center);
                return new OctreeNode(center, size, depth, material);
            }

            // Subdivide: create internal node with children
            Vector3[] childCenters = GetChildCenters(center, size);
            OctreeNode[] children = new OctreeNode[8];
            bool hasChildren = false;

            for (int i = 0; i < 8; i++)
            {
                float childSize = size / 2;
                children[i] = BuildNode(childCenters[i], childSize, depth + 1);
                if (children[i] != null)
                {
                    hasChildren = true;
                }
            }

            if (!hasChildren)
            {
                // All children are empty, this node is empty
                return null;
            }

            return new OctreeNode(center, size, depth, children);
        }

        /// <summary>
        /// Checks if an octree node intersects the asteroid shape.
        /// </summary>
        private bool NodeIntersectsAsteroid(Vector3 center, float size)
        {
            float halfSize = size / 2;

            // If cross-section cut is enabled, ignore anything with z > 0
            if (_enableCrossSectionCut && center.Z > 0)
            {
                // Check if any part of this node has z <= 0
                if (center.Z - halfSize > 0)
                {
                    return false; // Entirely in cut region
                }
            }

            // Sample multiple points on the cube surface to check intersection

            // Check corners and center
            Vector3[] checkPoints = new Vector3[]
            {
                center,
                center + new Vector3(halfSize, halfSize, halfSize),
                center + new Vector3(-halfSize, halfSize, halfSize),
                center + new Vector3(halfSize, -halfSize, halfSize),
                center + new Vector3(halfSize, halfSize, -halfSize),
                center + new Vector3(-halfSize, -halfSize, halfSize),
                center + new Vector3(-halfSize, halfSize, -halfSize),
                center + new Vector3(halfSize, -halfSize, -halfSize),
                center + new Vector3(-halfSize, -halfSize, -halfSize),
            };

            bool hasInside = false;
            bool hasOutside = false;

            foreach (var point in checkPoints)
            {
                // Check if point is inside asteroid shape
                float distFromCenter = point.Length();
                float deformation = _generator.GetSurfaceDeformation(point);
                float effectiveRadius = _asteroidRadius + deformation;

                if (distFromCenter < effectiveRadius)
                {
                    hasInside = true;
                }
                else
                {
                    hasOutside = true;
                }

                // Early exit if we have both inside and outside points
                if (hasInside && hasOutside)
                {
                    return true;
                }
            }

            // Node is entirely inside or entirely outside
            return hasInside; // If all points are inside, node intersects
        }

        /// <summary>
        /// Checks if a node is completely inside the asteroid.
        /// </summary>
        private bool NodeIsFullyInsideAsteroid(Vector3 center, float size)
        {
            // If cross-section cut is enabled, nodes on the z=0 plane are not fully inside
            if (_enableCrossSectionCut && center.Z + size / 2 > 0)
            {
                return false;
            }

            float halfSize = size / 2;

            // Check all corners are inside
            Vector3[] corners = new Vector3[]
            {
                center + new Vector3(halfSize, halfSize, halfSize),
                center + new Vector3(-halfSize, halfSize, halfSize),
                center + new Vector3(halfSize, -halfSize, halfSize),
                center + new Vector3(halfSize, halfSize, -halfSize),
                center + new Vector3(-halfSize, -halfSize, halfSize),
                center + new Vector3(-halfSize, halfSize, -halfSize),
                center + new Vector3(halfSize, -halfSize, -halfSize),
                center + new Vector3(-halfSize, -halfSize, -halfSize),
            };

            foreach (var corner in corners)
            {
                float distFromCenter = corner.Length();
                float deformation = _generator.GetSurfaceDeformation(corner);
                float effectiveRadius = _asteroidRadius + deformation;

                if (distFromCenter > effectiveRadius)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines the material at a given position.
        /// </summary>
        private MaterialEnum? DetermineMaterial(Vector3 position)
        {
            // Check cross-section cut first
            if (_enableCrossSectionCut && position.Z > 0)
            {
                return null;
            }

            return _generator.SampleMaterial(position);
        }

        /// <summary>
        /// Gets the 8 child centers for a given center and size.
        /// </summary>
        private Vector3[] GetChildCenters(Vector3 center, float size)
        {
            float offset = size / 4; // Quarter of parent size = half of child size

            return new Vector3[]
            {
                new Vector3(center.X - offset, center.Y - offset, center.Z - offset),
                new Vector3(center.X + offset, center.Y - offset, center.Z - offset),
                new Vector3(center.X - offset, center.Y + offset, center.Z - offset),
                new Vector3(center.X + offset, center.Y + offset, center.Z - offset),
                new Vector3(center.X - offset, center.Y - offset, center.Z + offset),
                new Vector3(center.X + offset, center.Y - offset, center.Z + offset),
                new Vector3(center.X - offset, center.Y + offset, center.Z + offset),
                new Vector3(center.X + offset, center.Y + offset, center.Z + offset)
            };
        }

        /// <summary>
        /// Samples the material at a world position.
        /// Returns null if empty, or the MaterialEnum if solid.
        /// </summary>
        public MaterialEnum? Sample(Vector3 worldPos)
        {
            // Check cross-section cut first
            if (_enableCrossSectionCut && worldPos.Z > 0)
            {
                return null;
            }

            // Traverse octree to find containing leaf
            return SampleNode(_root, worldPos);
        }

        /// <summary>
        /// Recursively samples a node for the material at a position.
        /// </summary>
        private MaterialEnum? SampleNode(OctreeNode node, Vector3 worldPos)
        {
            if (node == null)
            {
                return null;
            }

            // Check if position is within this node's bounds
            if (!node.GetBounds().HasPoint(worldPos))
            {
                return null;
            }

            if (node.IsLeaf)
            {
                return node.Material;
            }

            // Recurse into children
            foreach (var child in node.Children)
            {
                if (child != null)
                {
                    var result = SampleNode(child, worldPos);
                    if (result.HasValue)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all leaf nodes within a region.
        /// </summary>
        public List<OctreeNode> GetLeavesInRegion(Aabb region)
        {
            List<OctreeNode> leaves = new List<OctreeNode>();
            CollectLeavesInRegion(_root, region, leaves);
            return leaves;
        }

        /// <summary>
        /// Recursively collects leaf nodes within a region.
        /// </summary>
        private void CollectLeavesInRegion(OctreeNode node, Aabb region, List<OctreeNode> leaves)
        {
            if (node == null)
            {
                return;
            }

            if (!node.Intersects(region))
            {
                return;
            }

            if (node.IsLeaf)
            {
                leaves.Add(node);
                return;
            }

            foreach (var child in node.Children)
            {
                CollectLeavesInRegion(child, region, leaves);
            }
        }

        /// <summary>
        /// Gets leaf nodes at specific LOD levels based on distance from camera.
        /// LOD levels are determined by the lodDistances array:
        /// - lodDistances[0]: Distance at which to use max depth (finest detail)
        /// - lodDistances[n]: Distance at which to use depth = maxDepth - n
        /// </summary>
        public List<OctreeNode> GetLODNodes(Vector3 cameraPos, float[] lodDistances)
        {
            List<OctreeNode> nodes = new List<OctreeNode>();
            CollectLODNodes(_root, cameraPos, lodDistances, nodes);
            return nodes;
        }

        /// <summary>
        /// Recursively collects nodes at appropriate LOD levels.
        /// </summary>
        private void CollectLODNodes(OctreeNode node, Vector3 cameraPos, float[] lodDistances, List<OctreeNode> nodes)
        {
            if (node == null)
            {
                return;
            }

            // Calculate distance from camera to node center
            float distance = node.Center.DistanceTo(cameraPos);

            // Determine target depth based on distance
            int targetDepth = CalculateTargetDepth(distance, lodDistances);

            if (node.Depth >= targetDepth || node.IsLeaf)
            {
                // Use this node at this LOD level
                nodes.Add(node);
                return;
            }

            // Need finer detail - recurse into children
            foreach (var child in node.Children)
            {
                CollectLODNodes(child, cameraPos, lodDistances, nodes);
            }
        }

        /// <summary>
        /// Calculates the target octree depth based on distance from camera.
        /// </summary>
        private int CalculateTargetDepth(float distance, float[] lodDistances)
        {
            // Find the appropriate LOD level
            for (int i = 0; i < lodDistances.Length; i++)
            {
                if (distance <= lodDistances[i])
                {
                    // Use max depth at close distances
                    return _maxDepth;
                }
            }

            // Far away - reduce detail
            // Reduce by 1 for each LOD band
            int reduction = lodDistances.Length;
            for (int i = 0; i < lodDistances.Length; i++)
            {
                if (distance <= lodDistances[i] * 2) // Simple multiplier for further bands
                {
                    reduction = i;
                    break;
                }
            }

            return Math.Max(0, _maxDepth - reduction);
        }

        /// <summary>
        /// Gets the total number of leaf nodes in the octree.
        /// </summary>
        public int GetLeafCount()
        {
            return CountLeaves(_root);
        }

        /// <summary>
        /// Recursively counts leaf nodes.
        /// </summary>
        private int CountLeaves(OctreeNode node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node.IsLeaf)
            {
                return 1;
            }

            int count = 0;
            foreach (var child in node.Children)
            {
                count += CountLeaves(child);
            }
            return count;
        }

        /// <summary>
        /// Gets all leaf nodes in the octree (for debugging).
        /// </summary>
        public List<OctreeNode> GetAllLeaves()
        {
            List<OctreeNode> leaves = new List<OctreeNode>();
            CollectAllLeaves(_root, leaves);
            return leaves;
        }

        /// <summary>
        /// Recursively collects all leaf nodes.
        /// Filters out nodes with Z > 0 when cross-section cut is enabled.
        /// </summary>
        private void CollectAllLeaves(OctreeNode node, List<OctreeNode> leaves)
        {
            if (node == null)
            {
                return;
            }

            // Check cross-section cut - skip nodes entirely above z=0 plane
            if (_enableCrossSectionCut && node.Center.Z > 0)
            {
                float halfSize = node.Size / 2;
                // Only skip if the entire node is above z=0
                if (node.Center.Z - halfSize > 0)
                {
                    return;
                }
            }

            if (node.IsLeaf)
            {
                leaves.Add(node);
                return;
            }

            foreach (var child in node.Children)
            {
                CollectAllLeaves(child, leaves);
            }
        }
    }
}
