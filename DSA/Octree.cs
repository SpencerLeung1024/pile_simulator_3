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
    public bool IsRealVoxel // A parent can have a non-empty material (shown as a far away approximation) but the parent node shouldn't be used in gameplay calculations
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

    // Similar to Barnes-Hut approximation for gravity
    // Returns a list of nodes (possibly leaf, possibly internal) where farther nodes are larger
    public List<OctreeNode> QueryForLOD(Vector3 queryPos, float theta)
    {
        Stack<OctreeNode> stack = new Stack<OctreeNode>();
        List<OctreeNode> result = new List<OctreeNode>();

        stack.Push(Root);

        while (stack.Count > 0)
        {
            OctreeNode node = stack.Pop();
            float nodeTheta = node.Size / queryPos.DistanceTo(node.Center);
            if (nodeTheta < theta) // Case 1: This node is far enough away to approximate itself
            {
                result.Add(node);
            }
            else // Case 2: We need to explore children
            {
                if (node.IsRealVoxel) // Case 2a: This is a leaf node and is a real voxel. There are no children to explore, so we have to use this node even though it's close
                {
                    result.Add(node);
                }
                else // Case 2b: Proceed with children
                {
                    if (node.Children == null) // Realize children on demand
                    {
                        node.Children = new OctreeNode[8];
                        Vector3[] childCenters = node.GetChildCenters();
                        for (int i = 0; i < 8; i++)
                        {
                            MaterialEnum childMaterial = Generator.Sample(childCenters[i]);
                            node.Children[i] = new OctreeNode(childCenters[i], node.Height - 1, childMaterial);
                        }
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        stack.Push(node.Children[i]);
                    }
                }
            }
                
        }
        return result;
    }
}