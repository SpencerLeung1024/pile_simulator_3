using Godot;
using System;
using System.Collections.Generic;
using DSA;

public partial class Asteroid : Node3D
{
    // Scene references
    [Export] private PackedScene _rigidRockScene; // Real rocks that have been dislodged
    [Export] private PackedScene _staticRockScene; // Real rocks that are attached to the voxel grid
    [Export] private MultiMeshInstance3D _multiMeshRock; // Far away approximations of octree nodes

    // Configuration
    [Export] private float _radius = 100f;
    [Export] private ulong _seed = 12345;
    [Export] private int _maxDepth = 8;
    [Export] private bool _enableCrossSectionCut = false;
    [Export] private int _maxStaticRocks = 2000;
    [Export] private float _cameraMoveThreshold = 10f; // Minimum camera movement to trigger update

    // Internal state
    private VoxelOctree _octree;
    private float _realizationRadius = 50f; // Controlled by UI slider
    private Camera3D _camera;
    private HSlider _realDistanceSlider;
    private RichTextLabel _debugLabel;

    // Near zone: StaticBody3D rocks (inside realizationRadius)
    private Dictionary<Vector3I, Node3D> _staticRocks = new();
    private Queue<Node3D> _staticRockPool = new();

    // Far zone: MultiMesh data
    private List<OctreeNode> _farNodes = new();
    private bool _needsMultiMeshUpdate = true;

    // Camera tracking for optimization
    private Vector3 _lastCameraPosition = Vector3.Zero;

    // Debug stats
    private int _multiMeshCount = 0;
    private int _staticRockCount = 0;

    public override void _Ready()
    {
        // Initialize the octree with procedural generation
        _octree = new VoxelOctree(_seed, _radius, _maxDepth, _enableCrossSectionCut);
        GD.Print($"Asteroid octree initialized with {_octree.GetLeafCount()} leaf nodes");

        // Get camera reference from parent (World)
        Node parent = GetParent();
        if (parent != null)
        {
            _camera = parent.GetNodeOrNull<Camera3D>("Camera3D");
        }

        // Get UI references
        var ui = GetParent()?.GetNodeOrNull<Control>("UI");
        if (ui != null)
        {
            _realDistanceSlider = ui.GetNodeOrNull<HSlider>("RealDistanceSlider");
            _debugLabel = ui.GetNodeOrNull<RichTextLabel>("RichTextLabel");

            if (_realDistanceSlider != null)
            {
                _realizationRadius = (float)_realDistanceSlider.Value;
                _realDistanceSlider.ValueChanged += OnRealDistanceSliderChanged;
            }
        }

        // Ensure MultiMeshRock is set up
        if (_multiMeshRock == null)
        {
            _multiMeshRock = GetNodeOrNull<MultiMeshInstance3D>("MultiMeshRock");
        }

        // Initial update
        UpdateLOD();
    }

    public override void _Process(double delta)
    {
        if (_camera == null) return;

        // Check if camera moved enough to warrant an update
        float cameraMoveDistance = _camera.GlobalPosition.DistanceTo(_lastCameraPosition);
        if (cameraMoveDistance > _cameraMoveThreshold || _needsMultiMeshUpdate)
        {
            UpdateLOD();
            _lastCameraPosition = _camera.GlobalPosition;
        }

        // Update debug display
        UpdateDebugDisplay();
    }

    private void OnRealDistanceSliderChanged(double value)
    {
        _realizationRadius = (float)value;
        _needsMultiMeshUpdate = true;
        UpdateLOD();
    }

    /// <summary>
    /// Main LOD update logic. Determines which nodes are inside/outside realization radius
    /// and updates the visualization accordingly.
    /// </summary>
    private void UpdateLOD()
    {
        if (_camera == null || _octree == null) return;

        Vector3 cameraPos = _camera.GlobalPosition;

        // Get all leaf nodes
        List<OctreeNode> allLeaves = _octree.GetAllLeaves();

        // Separate nodes into near and far zones
        List<OctreeNode> nearNodes = new();
        _farNodes.Clear();

        foreach (var node in allLeaves)
        {
            // Skip nodes that don't have material (empty)
            if (!node.Material.HasValue) continue;

            float distanceToCamera = node.Center.DistanceTo(cameraPos);

            if (distanceToCamera <= _realizationRadius)
            {
                nearNodes.Add(node);
            }
            else
            {
                _farNodes.Add(node);
            }
        }

        // Limit near nodes to max static rocks
        if (nearNodes.Count > _maxStaticRocks)
        {
            // Sort by distance to camera (closest first)
            nearNodes.Sort((a, b) =>
            {
                float distA = a.Center.DistanceTo(cameraPos);
                float distB = b.Center.DistanceTo(cameraPos);
                return distA.CompareTo(distB);
            });

            // Move excess nodes to far list
            for (int i = _maxStaticRocks; i < nearNodes.Count; i++)
            {
                _farNodes.Add(nearNodes[i]);
            }
            nearNodes.RemoveRange(_maxStaticRocks, nearNodes.Count - _maxStaticRocks);
        }

        // Update static rocks for near nodes
        UpdateStaticRocks(nearNodes);

        // Update multimesh for far nodes
        if (_needsMultiMeshUpdate || cameraMoveDistanceSignificant())
        {
            UpdateMultiMesh();
            _needsMultiMeshUpdate = false;
        }

        _staticRockCount = _staticRocks.Count;
        _multiMeshCount = _farNodes.Count;
    }

    /// <summary>
    /// Updates static rock instances for nearby nodes.
    /// Spawns new rocks for nodes that don't have them, removes rocks for nodes that are now far.
    /// </summary>
    private void UpdateStaticRocks(List<OctreeNode> nearNodes)
    {
        HashSet<Vector3I> neededNodes = new();

        // Spawn or update rocks for near nodes
        foreach (var node in nearNodes)
        {
            Vector3I key = new Vector3I(
                Mathf.RoundToInt(node.Center.X * 1000),
                Mathf.RoundToInt(node.Center.Y * 1000),
                Mathf.RoundToInt(node.Center.Z * 1000)
            );

            neededNodes.Add(key);

            if (!_staticRocks.ContainsKey(key))
            {
                // Spawn new static rock
                Node3D rock = GetPooledRock();
                if (rock != null)
                {
                    rock.GlobalPosition = node.Center;
                    rock.Scale = Vector3.One * node.Size;

                    // Apply material color
                    MeshInstance3D meshInstance = rock.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
                    if (meshInstance != null && node.Material.HasValue)
                    {
                        int materialIndex = (int)node.Material.Value;
                        meshInstance.MaterialOverride = Materials.materialInstances[materialIndex];
                    }

                    _staticRocks[key] = rock;
                }
            }
        }

        // Remove rocks that are no longer needed
        List<Vector3I> keysToRemove = new();
        foreach (var kvp in _staticRocks)
        {
            if (!neededNodes.Contains(kvp.Key))
            {
                ReturnRockToPool(kvp.Value);
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _staticRocks.Remove(key);
        }
    }

    /// <summary>
    /// Updates the MultiMeshInstance3D with transforms and colors for far nodes.
    /// </summary>
    private void UpdateMultiMesh()
    {
        if (_multiMeshRock == null || _multiMeshRock.Multimesh == null) return;

        int count = _farNodes.Count;
        _multiMeshRock.Multimesh.InstanceCount = count;

        for (int i = 0; i < count; i++)
        {
            var node = _farNodes[i];

            // Create transform for this instance
            Transform3D transform = new Transform3D(
                Basis.FromScale(Vector3.One * node.Size),
                node.Center
            );

            _multiMeshRock.Multimesh.SetInstanceTransform(i, transform);

            // Set color based on material (or gray if unknown)
            Color color = new Color(0.5f, 0.5f, 0.5f); // Default gray
            if (node.Material.HasValue)
            {
                int materialIndex = (int)node.Material.Value;
                color = Materials.materialColors[materialIndex];
            }

            _multiMeshRock.Multimesh.SetInstanceColor(i, color);
        }
    }

    /// <summary>
    /// Gets a rock from the pool or instantiates a new one.
    /// </summary>
    private Node3D GetPooledRock()
    {
        if (_staticRockPool.Count > 0)
        {
            Node3D rock = _staticRockPool.Dequeue();
            rock.Visible = true;
            return rock;
        }

        if (_staticRockScene != null)
        {
            Node3D rock = _staticRockScene.Instantiate<Node3D>();
            AddChild(rock);
            return rock;
        }

        return null;
    }

    /// <summary>
    /// Returns a rock to the pool for reuse.
    /// </summary>
    private void ReturnRockToPool(Node3D rock)
    {
        if (rock != null)
        {
            rock.Visible = false;
            _staticRockPool.Enqueue(rock);
        }
    }

    /// <summary>
    /// Checks if camera has moved significantly enough to update multimesh.
    /// </summary>
    private bool cameraMoveDistanceSignificant()
    {
        // MultiMesh doesn't need frequent updates, only when nodes cross the threshold
        return false;
    }

    /// <summary>
    /// Updates the debug display with current stats.
    /// </summary>
    private void UpdateDebugDisplay()
    {
        if (_debugLabel == null) return;

        string text = $"FPS: {Engine.GetFramesPerSecond()}\n";

        if (_camera != null)
        {
            Vector3 pos = _camera.GlobalPosition;
            float distance = pos.DistanceTo(Vector3.Zero);
            text += $"({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})\n";
            text += $"Real Distance: {_realizationRadius:F0} m\n";
            text += $"Camera Dist: {distance:F2} m\n";
        }

        text += "---\n";
        text += $"MultiMesh: {_multiMeshCount}\n";
        text += $"Static: {_staticRockCount}\n";
        text += $"Rigid: 0\n";
        text += "---\n";

        // Count materials in near nodes
        int rockCount = 0, iceCount = 0, metalCount = 0;
        foreach (var kvp in _staticRocks)
        {
            MeshInstance3D meshInstance = kvp.Value.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
            if (meshInstance != null && meshInstance.MaterialOverride is StandardMaterial3D mat)
            {
                Color color = mat.AlbedoColor;
                if (color == Materials.materialColors[0]) rockCount++;
                else if (color == Materials.materialColors[1]) iceCount++;
                else if (color == Materials.materialColors[2]) metalCount++;
            }
        }

        text += $"Rock: {rockCount}\n";
        text += $"Ice: {iceCount}\n";
        text += $"Metal: {metalCount}";

        _debugLabel.Text = text;
    }

    /// <summary>
    /// Sets the realization radius (can be called from external scripts).
    /// </summary>
    public void SetRealizationRadius(float radius)
    {
        _realizationRadius = radius;
        _needsMultiMeshUpdate = true;
        UpdateLOD();
    }

    /// <summary>
    /// Gets the current realization radius.
    /// </summary>
    public float GetRealizationRadius()
    {
        return _realizationRadius;
    }

    /// <summary>
    /// Sets whether to enable the cross-section cut at z=0.
    /// </summary>
    public void SetCrossSectionCut(bool enable)
    {
        _enableCrossSectionCut = enable;
        if (_octree != null)
        {
            _octree.EnableCrossSectionCut = enable;
            _needsMultiMeshUpdate = true;
            UpdateLOD();
        }
    }

    /// <summary>
    /// Gets the underlying voxel octree.
    /// </summary>
    public VoxelOctree GetOctree()
    {
        return _octree;
    }
}
