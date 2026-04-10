using Godot;
using System;
using System.Collections.Generic;

// Lesson from v1: enums are pass by value, so the octree was recreating the root node each time
// Increase memory locality by creating a pool or something
public class OctreeNode
{
    public Vector3 Center;
    public int Height; // 0 = leaf node, 1 = parent of 8 leaf nodes, etc.
    public int Size // Height 0 = 1 m, Height 1 = 2 m, Height 2 = 4 m, etc.
    {
        get
        {
            return 1 << Height;
        }
    }
    public OctreeNode[] Children; // May be null and realized on demand to answer a query
    public MaterialEnum Material; // Empty = -1 so treat -1 as the nothing state
    public bool IsRealVoxel // An internal node can have a non-empty material (shown as a far away approximation) but the internal node shouldn't be used in gameplay calculations
    {
        get
        {
            return Height == 0;
        }
    }

    public OctreeNode(Vector3 center, int height, MaterialEnum material)
    {
        Center = center;
        Height = height;
        Children = null;
        Material = material;
    }

    public Aabb GetBounds()
    {
        Vector3 halfSize = new Vector3(Size / 2, Size / 2, Size / 2);
        return new Aabb(Center - halfSize, halfSize * 2);
    }

    // Returns true if this node's bounding box is completely outside the given radius
    // Used for conservative empty-space culling
    public bool IsOutsideRadius(float radius)
    {
        // Find the closest point in the node's AABB to the origin
        Vector3 halfSize = new Vector3(Size / 2, Size / 2, Size / 2);
        Vector3 min = Center - halfSize;
        Vector3 max = Center + halfSize;
        
        // Closest point on AABB to origin
        Vector3 closest = new Vector3(
            Mathf.Clamp(0, min.X, max.X),
            Mathf.Clamp(0, min.Y, max.Y),
            Mathf.Clamp(0, min.Z, max.Z)
        );
        
        // If closest point is outside radius, entire AABB is outside
        return closest.Length() > radius;
    }

    public bool Intersects(Aabb bounds)
    {
        return GetBounds().Intersects(bounds);
    }

    public Vector3[] GetChildCenters()
    {
        float childSize = (float) (Size / 2);
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

    public int GetOctant(Vector3 point)
    {
        int octant = 0;
        if (point.X >= Center.X) octant |= 1; // +x
        if (point.Y >= Center.Y) octant |= 2; // +y
        if (point.Z >= Center.Z) octant |= 4; // +z
        return octant;
    }
}

public class Octree
{
    public OctreeNode Root;
    public AsteroidGenerator Generator;

    public Octree(AsteroidGenerator generator)
    {
        Generator = generator;
        int rootHeight = (int) MathF.Ceiling(MathF.Log2(Generator.MaxRadius * 2)); // Ensure the root node can encompass the entire asteroid
        MaterialEnum rootMaterial = Generator.Sample(Vector3.Zero); // Sample the center of the asteroid to determine root material
        Root = new OctreeNode(Vector3.Zero, rootHeight, rootMaterial);
    }

    private void RealizeChildren(OctreeNode node)
    {
        if (node.IsRealVoxel) return; // Can't realize children of a leaf node
        
        node.Children = new OctreeNode[8];
        Vector3[] childCenters = node.GetChildCenters();
        for (int i = 0; i < 8; i++)
        {
            MaterialEnum childMaterial = Generator.Sample(childCenters[i]);
            node.Children[i] = new OctreeNode(childCenters[i], node.Height - 1, childMaterial);
        }
    }

    // Used to get the node that the point occupies
    // Default stopHeight 0 means drill down to the real voxel
    // Can be used with higher stop heights to get an internal node of a given size instead
    public OctreeNode? Query(Vector3 point, float stopHeight = 0)
    {
        OctreeNode currentNode = Root;
        // Check bounds
        if (!currentNode.Intersects(new Aabb(point, Vector3.Zero)))
        {
            return null; // Point is outside the bounds of the octree
        }
        while (currentNode != null && currentNode.Height > stopHeight)
        {
            if (currentNode.Children == null) // Realize children on demand. This can never happen on a leaf node (real voxel) because currentNode.Height = 0 !> 0
            {
                RealizeChildren(currentNode);
            }
            int octant = currentNode.GetOctant(point);
            currentNode = currentNode.Children[octant];
        }
        // Exit the loop when we hit a null child or a leaf node (height 0)
        return currentNode;
    }

    // Gets 6 neighbors: [-x, -y, -z, +x, +y, +z].
    // Has a really messy path for accepting a cache to hopefully speed up rendering
    // No node has the same center as any other node. Children of a layer have centers offset by a quarter of the parent's size. They form a lattice half the size and offset a quarter compared to the parent lattice
    public OctreeNode[] GetNeighbors(OctreeNode node, Dictionary<Vector3, OctreeNode>? cache = null)
    {
        Vector3[] queryPoints = new Vector3[]
        {
            node.Center + new Vector3(-node.Size, 0, 0), // -x
            node.Center + new Vector3(0, -node.Size, 0), // -y
            node.Center + new Vector3(0, 0, -node.Size), // -z
            node.Center + new Vector3(node.Size, 0, 0), // +x
            node.Center + new Vector3(0, node.Size, 0), // +y
            node.Center + new Vector3(0, 0, node.Size) // +z
        };
        OctreeNode[] neighbors = new OctreeNode[6];
        for (int i = 0; i < 6; i++)
        {
            if (cache != null && cache.TryGetValue(queryPoints[i], out OctreeNode cachedNode))
            {
                neighbors[i] = cachedNode;
            }
            else
            {
                neighbors[i] = Query(queryPoints[i], node.Height);
                if (cache != null)
                {
                    cache[queryPoints[i]] = neighbors[i];
                }
            }
        }
        return neighbors;
    }

    private int NonEmpty(OctreeNode[] nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node != null && node.Material != MaterialEnum.Empty)
            {
                count++;
            }
        }
        return count;
    }

    // Similar to Barnes-Hut approximation for gravity
    // Returns a list of nodes (possibly leaf, possibly internal) where farther nodes are larger
    public List<OctreeNode> QueryForLOD(Vector3 queryPos, float theta, bool neighborCulling)
    {
        Stack<OctreeNode> stack = new Stack<OctreeNode>();
        List<OctreeNode> result = new List<OctreeNode>();
        Dictionary<Vector3, OctreeNode> neighborCache = new Dictionary<Vector3, OctreeNode>();
        
        int nodesVisited = 0;
        int GetNeighborCalls = 0;
        int[] debugCount = new int[7];
        
        stack.Push(Root);

        while (stack.Count > 0)
        {
            OctreeNode node = stack.Pop();

            nodesVisited++;

            float nodeTheta = node.Size / queryPos.DistanceTo(node.Center);
            if (nodeTheta < theta) // Case 1: This node is far enough away to approximate itself
            {
                if (node.Material != MaterialEnum.Empty) // This node approximates an empty region. We don't care about any bits that far away so we can skip it
                {
                    bool passedNeighborCheck = true;
                    if (neighborCulling)
                    {
                        GetNeighborCalls++;
                        OctreeNode[] neighbors = GetNeighbors(node, neighborCache);
                        int nonEmptyNeighbors = NonEmpty(neighbors);
                        debugCount[nonEmptyNeighbors]++;
                        if (nonEmptyNeighbors == 6)
                        {
                            passedNeighborCheck = false; // All neighbors are solid
                        }
                    }
                    if (passedNeighborCheck)
                    {
                        result.Add(node);
                    }
                }
            }
            else // Case 2: We need to explore children
            {
                // OPTIMIZATION: Skip nodes that are completely outside the asteroid's max radius
                // Since asteroid is a heightfield (no caves), if the entire node bounds are outside MaxRadius,
                // all descendants will be empty
                if (node.IsOutsideRadius(Generator.MaxRadius))
                {
                    continue; // Entire node volume is outside asteroid - skip
                }

                if (node.IsRealVoxel) // Case 2a: This is a leaf node and is a real voxel. There are no children to explore, so we have to use this node even though it's close
                {
                    if (node.Material != MaterialEnum.Empty)
                    {
                        bool passedNeighborCheck = true;
                        if (neighborCulling)
                        {
                            GetNeighborCalls++;
                            OctreeNode[] neighbors = GetNeighbors(node, neighborCache);
                            int nonEmptyNeighbors = NonEmpty(neighbors);
                            debugCount[nonEmptyNeighbors]++;
                            if (nonEmptyNeighbors == 6)
                            {
                                passedNeighborCheck = false; // All neighbors are solid
                            }
                        }
                        if (passedNeighborCheck)
                        {
                            result.Add(node);
                        }
                    }
                }
                else // Case 2b: Proceed with children
                {
                    // Note: Nodes approximating an empty region (because their center is empty according to the generator) may contain descendants with real material
                    if (node.Children == null) // Realize children on demand
                    {
                        RealizeChildren(node);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        stack.Push(node.Children[i]);
                    }
                }
            }
                
        }

        GD.Print($"Visited {nodesVisited} nodes");
        GD.Print($"Called GetNeighbors {GetNeighborCalls} times");
        GD.Print($"neighbors {string.Join(", ", debugCount)}");
        GD.Print($"Neighbor cache has {neighborCache.Count} entries");
        GD.Print($"Returned {result.Count} nodes");

        // 500 m radius asteroid
        // 500 m + (0.8 * 500 m) = 900 m max radius = 2048 m root = height 11 root

        // At the initial spawn point 1000 m away
        /*
        Visited 3632201 nodes
        Called GetNeighbors 269603 times
        neighbors 0, 30, 264, 4224, 5292, 7449, 252344
        Neighbor cache has 298890 entries
        Returned 17259 nodes
        */

        // About 45 m from the surface
        /*
        Visited 16163489 nodes
        Called GetNeighbors 3470369 times
        neighbors 0, 52, 448, 17017, 21658, 45481, 3385713
        Neighbor cache has 3709953 entries
        Returned 84656 nodes
        */

        return result;
    }
}