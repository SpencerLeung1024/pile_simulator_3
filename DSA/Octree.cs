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
	public bool IsRealVoxel // An internal node can have a non-empty material (shown as a far away approximation) but the internal node shouldn't be used in gameplay calculations
	{
		get
		{
			return Height == 0;
		}
	}
	public OctreeNode Parent; // Null for the root node
	public OctreeNode[] Children; // May be null and realized on demand to answer a query
	public MaterialEnum Material; // Empty = -1 so treat -1 as the nothing state
	//public bool MayHaveSolidDescendants; // Set when realized. Can be false if Material is not Empty or the bounding box lies inside the asteroid's max radius
	public bool Mixed; // Supersedes MayHaveSolidDescendants. Mixed can be used to check both truly empty nodes all the way down, and truly solid nodes
	// A leaf node can never have children and cannot be mixed
	// A node is mixed if at least one child is mixed
	// If a node is not mixed, all descendants are either empty or solid and are also not mixed
	public bool IsTrulyEmpty
	{
		get
		{
			return !Mixed && Material == MaterialEnum.Empty;
		}
	}
	public bool IsTrulySolid
	{
		get
		{
			return !Mixed && Material != MaterialEnum.Empty;
		}
	}
	public byte ExposedFaces; // [_, _, +z, +y, +x, -z, -y, -x]. Can do == 0x00 to check if this is unreachable from the outside
	// This is probably gonna result in a mishap in the future but:
	// 0xff [11111111] is a special value meaning "not yet determined"
	// The culling check uses it to fill in the ExposedFaces field so it doesn't have to do expensive GetNeighbors calls in the future
	

	public OctreeNode(Vector3 center, int height, OctreeNode parent, MaterialEnum material, bool mixed, byte exposedFaces)
	{
		Center = center;
		Height = height;
		Parent = parent;
		Children = null;
		Material = material;
		Mixed = mixed;
		ExposedFaces = exposedFaces;
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

	// Geometry checks

	// Aabbs have position = left, bottom, back corner
	public Aabb GetAabb(bool includeRightTopFront = false)
	{
		float rightTopFrontSize = includeRightTopFront ? Size : Size + 0.001f; // Add a bit to ensure points on the right, top, and front faces are included
		return new Aabb
		(
			Center - new Vector3(Size / 2, Size / 2, Size / 2),
			new Vector3(rightTopFrontSize, rightTopFrontSize, rightTopFrontSize)
		);
	}

	public bool HasPoint(Vector3 point, bool includeRightTopFront = false)
	{
		return GetAabb(includeRightTopFront).HasPoint(point);
	}

	public bool Intersects(Aabb bounds)
	{
		return GetAabb().Intersects(bounds);
	}

	// Returns true if this node's bounding box is completely outside the given radius
	// Used for conservative empty-space culling
	public bool IsOutsideRadius(float radius)
	{
		Aabb bounds = GetAabb();
		Vector3 min = bounds.Position;
		Vector3 max = bounds.Position + bounds.Size;
		
		// Closest point on AABB to origin
		Vector3 closest = new Vector3(
			Mathf.Clamp(0, min.X, max.X),
			Mathf.Clamp(0, min.Y, max.Y),
			Mathf.Clamp(0, min.Z, max.Z)
		);
		
		// If closest point is outside radius, entire AABB is outside
		return closest.Length() > radius;
	}

	// The opposite, for enclosed nodes deep within the asteroid
	public bool IsInsideRadius(float radius)
	{
		Aabb bounds = GetAabb();
		Vector3 normal = Center.Normalized();
		Vector3 support = bounds.GetSupport(normal); // "Returns the vertex's position of this bounding box that's the farthest in the given direction. This point is commonly known as the support point in collision detection algorithms."
		return support.Length() < radius;
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
		Root = new OctreeNode(Vector3.Zero, rootHeight, null, rootMaterial, true, 0x3f); // The root encompasses the entire asteroid and faces space, so all faces are exposed
	}

	private void RealizeChildren(OctreeNode node)
	{
		if (node.IsRealVoxel) return; // Can't realize children of a leaf node
		
		node.Children = new OctreeNode[8];
		Vector3[] childCenters = node.GetChildCenters();
		for (int i = 0; i < 8; i++)
		{
			MaterialEnum childMaterial = Generator.Sample(childCenters[i]);
			OctreeNode child = new OctreeNode(childCenters[i], node.Height - 1, node, childMaterial, true, 0xff); // We don't know the mixed state of the child's children yet. Also whether it is exposed is unknown right now

			// However, in certain cases, we can determine the mixed state
			// Case 1: The child is a leaf node. It cannot be mixed
			if (child.IsRealVoxel)
			{
				child.Mixed = false;
			}
			// Case 2: The child is completely outside the asteroid. It is truly empty all the way down
			else if (child.IsOutsideRadius(Generator.MaxRadius))
			{
				child.Mixed = false;
			}
			// Case 3: The child is completely inside the asteroid. It is truly solid all the way down
			else if (child.IsInsideRadius(Generator.MinRadius))
			{
				child.Mixed = false;
			}

			node.Children[i] = child;
		}
	}

	// Used to get the node that the point occupies
	// Default stopHeight 0 means drill down to the real voxel
	// Can be used with higher stop heights to get an internal node of a given size instead
	public OctreeNode? Query(Vector3 point, float stopHeight = 0, bool stopOnTrulyEmpty = true, bool stopOnTrulySolid = false)
	{
		OctreeNode currentNode = Root;
		// Check bounds
		if (!currentNode.HasPoint(point, true))
		{
			return null; // Point is outside the bounds of the octree
		}
		while (currentNode != null && currentNode.Height > stopHeight)
		{
			// Do the truly empty or solid checks
			if ((stopOnTrulyEmpty && currentNode.IsTrulyEmpty) || (stopOnTrulySolid && currentNode.IsTrulySolid))
			{
				break;
			}
			if (currentNode.Children == null) // Realize children on demand. This can never happen on a leaf node (real voxel) because currentNode.Height = 0 !> 0
			{
				RealizeChildren(currentNode);
			}
			int octant = currentNode.GetOctant(point);
			currentNode = currentNode.Children[octant];
		}
		// Exit the loop when we hit a null child, a leaf node (height 0), or one of the stopping simplifications
		return currentNode;
	}

	// Samet's octree neighbor algorithm (1989)
	// Reference implementation in reports/better_traversal/opus_4_6.md
	// See reports/better_traversal/samet_algorithm_workthrough.jpg for a workthrough (using a quadtree because I can't draw in 3D) on paper
	// I suppose you could do it by checking if queryPos and neighborPos are both within currentNode, and use repeated GetOctant() on the way down
	// Bit manipulation should be faster though
	// face: 5=+z, 4=+y, 3=+x, 2=-z, 1=-y, 0=-x (bit index of ExposedFaces)
	public OctreeNode? GetFaceNeighbor(OctreeNode node, int face)
	{
		int axis = face % 3; // Which axis to mirror. 2=z, 1=y, 0=x
		byte mask = (byte)(1 << axis); // 0x04=z, 0x02=y, 0x01=x
		bool positive = face >= 3; // If true, we want to step in the +x/+y/+z direction
		// node must have octant & mask == 0 (the lesser side of the common parent) and neighbor must have octant & mask != 0 (the greater side)
		// If not positive:
		// node must be != 0 (greater) and neighbor must be == 0 (lesser)

		// Walk up until octant & mask == 0 (if positive) or != 0 (if negative)
		OctreeNode currentNode = node;
		Stack<int> path = new Stack<int>(); // Keep track of the path we took up so we can mirror it on the way down

		while (currentNode.Parent != null)
		{
			int octant = currentNode.Parent.GetOctant(currentNode.Center); // We don't store the child's octant in the child so we have to do GetOctant calls on the way up
			path.Push(octant);
			bool canStepAcross = positive ? (octant & mask) == 0 : (octant & mask) != 0;
			currentNode = currentNode.Parent; // Step up here so the break below is after stepping up
			if (canStepAcross)
			{
				break;
			}
		}

		if (currentNode == null) // Reached the root. The desired neighbor is outside the bounds
		{
			return null;
		}

		// Otherwise mirror the path using the mask and walk down
		while (path.Count > 0)
		{
			int octant = path.Pop(); // The octant the query node came from
			int mirroredOctant = octant ^ mask; // Flip the bit corresponding to the axis to get the octant the neighbor should be in
			if (currentNode.Children == null) // Realize children on demand. This can never happen on a leaf node (real voxel) because we cannot walk down more times than we walked up
			{
				RealizeChildren(currentNode);
			}
			currentNode = currentNode.Children[mirroredOctant];
		}
		
		return currentNode;
	}

	// Supersedes the previous weird mechanism
	// This stores the result in the node's ExposedFaces field
	public byte GetExposedFaces(OctreeNode node)
	{
		if (node.ExposedFaces != 0xff) // Special value meaning "not yet determined"
		{
			return node.ExposedFaces;
		}
		else
		{
			// Otherwise calculate it
			byte exposedFaces = 0;
			/*
			Vector3[] queryPoints = new Vector3[]
			{
				node.Center + new Vector3(-node.Size, 0, 0), // -x
				node.Center + new Vector3(0, -node.Size, 0), // -y
				node.Center + new Vector3(0, 0, -node.Size), // -z
				node.Center + new Vector3(node.Size, 0, 0), // +x
				node.Center + new Vector3(0, node.Size, 0), // +y
				node.Center + new Vector3(0, 0, node.Size) // +z
			};
			*/
			// GetFaceNeighbor doesn't use queryPoints
			for (int i = 0; i < 6; i++)
			{
				//OctreeNode neighbor = Query(queryPoints[i], node.Height, true, true);
				// StopOnTrulyEmpty because any node inside it is necessarily empty
				// Also StopOnTrulySolid because we're not interested in the actual material of the neighbor, only that it's solid. It will be solid if a parent is truly solid
				OctreeNode neighbor = GetFaceNeighbor(node, i);
				// This should be faster
				if (neighbor == null || neighbor.Material == MaterialEnum.Empty)
				{
					exposedFaces |= (byte)(1 << i); // This face is exposed to space
				}
			}
			node.ExposedFaces = exposedFaces;
			return exposedFaces;
		}
	}

	// Similar to Barnes-Hut approximation for gravity
	// Returns a list of nodes (possibly leaf, possibly internal) where farther nodes are larger
	public List<OctreeNode> QueryForLOD(Vector3 queryPos, float theta, bool neighborCulling)
	{
		Stack<OctreeNode> stack = new Stack<OctreeNode>();
		List<OctreeNode> result = new List<OctreeNode>();
		
		// Keep track of paths
		int visitedNodes = 0;
			int leafNodes = 0; // In Python this wouldn't work but in C# I can use whitespace for whatever I want
				int leafEmpty = 0;
				int leafSolid = 0;
					int leafEnclosed = 0;
					int leafExposed = 0;
			int internalNodes = 0;
				int trulyEmpty = 0;
				int trulySolid = 0;
					int solidEnclosed = 0;
					int solidExposed = 0;
				int thetaPassed = 0;
					int thetaEmpty = 0;
					int thetaSolid = 0;
						int thetaEnclosed = 0;
						int thetaExposed = 0;
				int thetaFailed = 0;
		
		stack.Push(Root);

		// Rewrite 3: Do the neighbor culling check at the end of the paths, rather than grouping them all in a node.Material != MaterialEnum.Empty at the beginning
		// This lets the debug info track if a leaf, truly solid, or possibly solid node triggered the check
		// Also move leaf processing back into the main loop, rather than getting their height 1 parent to handle them
		// Getting information (material, etc.) from a leaf will require a pointer dereference anyways, unless a future rewrite stores leaf information in the 64 bits of children[i]
		while (stack.Count > 0)
		{
			OctreeNode node = stack.Pop();
			visitedNodes++;

			if (node.IsRealVoxel)
			{
				leafNodes++;
				if (node.Material == MaterialEnum.Empty)
				{
					leafEmpty++;
				}
				else
				{
					leafSolid++;
					// We're gonna have to use the same neighbor culling logic three times
					// Hopefully this doesn't blow up the icache when turned into machine code
					byte exposedFaces = neighborCulling ? GetExposedFaces(node) : (byte)0x3f; // If neighbor culling is off, treat all faces as exposed
					if (exposedFaces == 0x00)
					{
						leafEnclosed++;
					}
					else
					{
						leafExposed++;
						result.Add(node);
					}
				}
			}
			else
			{
				internalNodes++;
				if (node.IsTrulyEmpty)
				{
					trulyEmpty++;
					continue; // Don't explore truly empty nodes
				}
				else if (node.IsTrulySolid)
				{
					trulySolid++;
					byte exposedFaces = neighborCulling ? GetExposedFaces(node) : (byte)0x3f;
					if (exposedFaces == 0x00)
					{
						solidEnclosed++;
						continue; // This node and its children will not be visible from the outside
					}
					else
					{
						solidExposed++;
						// But do not add this node. We don't know if this node is close enough that we want to break it apart into children
						// The below checks will handle that, so proceed normally
					}
				}
				float nodeTheta = node.Size / queryPos.DistanceTo(node.Center);
				if (nodeTheta < theta)
				{
					thetaPassed++;
					if (node.Material == MaterialEnum.Empty)
					{
						thetaEmpty++;
					}
					else
					{
						thetaSolid++;
						byte exposedFaces = neighborCulling ? GetExposedFaces(node) : (byte)0x3f;
						if (exposedFaces == 0x00)
						{
							thetaEnclosed++;
						}
						else
						{
							thetaExposed++;
							result.Add(node);
						}
					}
				}
				else
				{
					thetaFailed++;
					if (node.Children == null) // Realize children on demand. This is safe here because if this was a leaf node, it would have gone to the path above
					{
						RealizeChildren(node);
					}
					for (int i = 0; i < 8; i++) // Stack doesn't have AddRange so we need to do our own loop
					{
						stack.Push(node.Children[i]);
					}
				}
			}
		}

		int terminusEmpty = leafEmpty + trulyEmpty + thetaEmpty;
		int terminusSolid = leafSolid + trulySolid + thetaSolid;
		int terminusEnclosed = leafEnclosed + solidEnclosed + thetaEnclosed;
		int terminusExposed = leafExposed + thetaExposed; // solidExposed is not a terminal path

		Dictionary<string, string> debugInfo = Settings.GetSettings().DebugInfo;
		// This is changed often so format TraversalLines here instead of formatting in UIController.cs
		debugInfo["TraversalLines"] = $@"Visited: {visitedNodes}
  Leaf: {leafNodes}
    Empty: {leafEmpty} Solid: {leafSolid}
      Enclosed: {leafEnclosed} Exposed: {leafExposed}
  Internal: {internalNodes}
    Truly Empty: {trulyEmpty}
    Truly Solid: {trulySolid}
      Enclosed: {solidEnclosed} Exposed*: {solidExposed}
    Theta Passed: {thetaPassed}
      Empty: {thetaEmpty} Solid: {thetaSolid}
        Enclosed: {thetaEnclosed} Exposed: {thetaExposed}
    Theta Failed: {thetaFailed}
Terminus:
  Empty: {terminusEmpty} Solid: {terminusSolid}
	Enclosed: {terminusEnclosed} Exposed: {terminusExposed}".Replace(System.Environment.NewLine, "\n"); // RichTextLabel handles \r\n fine, but GD.Print produces two newlines

		return result;
	}

	// Post-order traversal
	// For each node, check if all children are truly empty (MaterialEnum.Empty and not Mixed)
	// or truly solid (other MaterialEnum and not Mixed)
	// If so, set the parent node to that material and not mixed
	// Also optionally delete children to save memory. Note that certain queries can remake children later
	// Not automatically done. There should be a button on the UI to call this
	public void Consolidate(bool deleteChildren = true)
	{
		// Iterative post-order traversal using explicit stack
		// Each entry tracks: node, whether children have been processed, the node's parent
		var stack = new Stack<(OctreeNode node, OctreeNode parent, bool processed)>();
		
		stack.Push((Root, null, false));
		
		while (stack.Count > 0)
		{
			var (node, parent, processed) = stack.Pop();
			
			// Skip leaf nodes - nothing to consolidate
			if (node.IsRealVoxel || node.Children == null)
				continue;
			
			if (!processed)
			{
				// First visit: push node back as "processed", then push all children
				stack.Push((node, parent, true));
				
				// Push children (they'll be processed before the parent due to LIFO)
				for (int i = 0; i < 8; i++)
				{
					if (node.Children[i] != null)
					{
						stack.Push((node.Children[i], node, false));
					}
				}
			}
			else
			{
				// Second visit: children have been processed, try to consolidate
				TryConsolidateNode(node, deleteChildren);
			}
		}
	}
	
	private void TryConsolidateNode(OctreeNode node, bool deleteChildren)
	{
		if (node.Children == null) return;
		
		// Check if all children are uniform (all truly empty or all truly solid with same material)
		bool allEmpty = true;
		bool allSolid = true;
		MaterialEnum? solidMaterial = null;
		bool differentMaterials = false;
		
		for (int i = 0; i < 8; i++)
		{
			var child = node.Children[i];
			if (child == null) continue;
			
			// If any child is still mixed, we can't consolidate
			if (child.Mixed)
			{
				return;
			}
			
			if (child.Material == MaterialEnum.Empty)
			{
				allSolid = false;
			}
			else
			{
				allEmpty = false;
				if (!differentMaterials)
				{
					if (solidMaterial == null)
					{
						solidMaterial = child.Material;
					}
					else if (solidMaterial != child.Material) // Found a different material. Don't change the parent's material, but do set the parent's Mixed to false
					{
						differentMaterials = true;
					}
				}
			}
		}

		// Failure modes that theoretically shouldn't happen
		if (allEmpty && allSolid)
		{
			return;
		}
		else if (!allEmpty && !allSolid)
		{
			return;
		}
		
		// Set the parent's material
		if (allEmpty)
		{
			node.Material = MaterialEnum.Empty;
		}
		else if (allSolid && !differentMaterials && solidMaterial.HasValue)
		{
			node.Material = solidMaterial.Value;
		}
		
		node.Mixed = false;
		
		if (deleteChildren)
		{
			node.Children = null;
		}
	}
}
