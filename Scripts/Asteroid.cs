using Godot;
using System;
using System.Collections.Generic;

public partial class Asteroid : Node3D
{
    // Scene references
    [Export] private PackedScene _rigidRockScene; // Real rocks that have been dislodged
    [Export] private PackedScene _staticRockScene; // Real rocks that are attached to the voxel grid
    [Export] private MultiMeshInstance3D _multiMeshRock; // Far away approximations of octree nodes

    // Configuration
    [Export] private float _radius = 100f;
    [Export] private ulong _seed = 12345;
    //[Export] private int _maxDepth = 8;
    [Export] private bool _enableCrossSectionCut = false;
    [Export] private int _maxStaticRocks = 10000;
    [Export] private float _cameraMoveThreshold = 10f; // Minimum camera movement to trigger update
    [Export] private float _thetaThreshold = 0.5f; // Barnes-Hut theta threshold for LOD

    // Internal state
    private Octree _octree;
    private AsteroidGenerator _generator;
    private float _realizationRadius = 50f; // Controlled by UI slider
    private Camera3D _camera;
    private HSlider _realDistanceSlider;
    private HSlider _maxStaticRocksSlider;
    private CheckButton _neighborCullingCheck;
    private RichTextLabel _debugLabel;
    private bool _neighborCullingEnabled = false;

    // Near zone: StaticBody3D rocks (inside realizationRadius)
    private List<OctreeNode> _nearNodes = new();
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
        float bulkDensity = 2.5f;
        float idealVolume = (4.0f / 3.0f) * Mathf.Pi * Mathf.Pow(_radius, 3);
        float gravitationalMass = bulkDensity * idealVolume;
        float maxHeight = _radius * 0.2f;
        // Initialize asteroid generator
        _generator = new AsteroidGenerator(_seed, _radius, gravitationalMass, maxHeight);
        // Initialize the octree with procedural generation
        _octree = new Octree(_generator);

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
            _maxStaticRocksSlider = ui.GetNodeOrNull<HSlider>("MaxStaticRocksSlider");
            _neighborCullingCheck = ui.GetNodeOrNull<CheckButton>("NeighborCullingCheck");
            _debugLabel = ui.GetNodeOrNull<RichTextLabel>("RichTextLabel");

            if (_realDistanceSlider != null)
            {
                _realizationRadius = (float)_realDistanceSlider.Value;
                _realDistanceSlider.ValueChanged += OnRealDistanceSliderChanged;
            }

            if (_maxStaticRocksSlider != null)
            {
                _maxStaticRocks = (int)_maxStaticRocksSlider.Value;
                _maxStaticRocksSlider.ValueChanged += OnMaxStaticRocksSliderChanged;
            }

            if (_neighborCullingCheck != null)
            {
                _neighborCullingEnabled = _neighborCullingCheck.ButtonPressed;
                _neighborCullingCheck.Toggled += OnNeighborCullingToggled;
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

    private void OnMaxStaticRocksSliderChanged(double value)
    {
        _maxStaticRocks = (int)value;
        _needsMultiMeshUpdate = true;
        UpdateLOD();
    }

    private void OnNeighborCullingToggled(bool enabled)
    {
        _neighborCullingEnabled = enabled;
        _needsMultiMeshUpdate = true;
        UpdateLOD();
    }

    /// <summary>
    /// Main LOD update logic using Barnes-Hut style theta-based approximation.
    /// Uses GetVisibleNodes which renders larger blocks for distant areas (O(log d) instead of O(d²)).
    /// </summary>
    private void UpdateLOD()
    {
        if (_camera == null || _octree == null) return;

        Vector3 cameraPos = _camera.GlobalPosition;

        List<OctreeNode> visibleNodes = _octree.QueryForLOD(cameraPos, _thetaThreshold);

        _nearNodes.Clear();
        _farNodes.Clear();

        foreach (OctreeNode node in visibleNodes)
        {
            if (node.Material == MaterialEnum.Empty) continue; // Empty nodes are not rendered

            if (node.IsRealVoxel)
            {
                _nearNodes.Add(node);
            }
            else
            {
                _farNodes.Add(node);
            }
        }

        // If there are too many near nodes, bump the farthest ones to far zone
        if (_nearNodes.Count > _maxStaticRocks)
        {
            List<OctreeNode> sortedNearNodes = new List<OctreeNode>(_nearNodes);
            sortedNearNodes.Sort((a, b) =>
            { float distA = a.Center.DistanceTo(cameraPos);
                float distB = b.Center.DistanceTo(cameraPos);
                return distA.CompareTo(distB);
            });

            _farNodes.AddRange(sortedNearNodes.GetRange(_maxStaticRocks, sortedNearNodes.Count - _maxStaticRocks));
            _nearNodes = sortedNearNodes.GetRange(0, _maxStaticRocks);
        }

        UpdateStaticRocks();
        UpdateMultiMesh();
    }

    /// <summary>
    /// Updates static rock instances for nearby nodes.
    /// Spawns new rocks for nodes that don't have them, removes rocks for nodes that are now far.
    /// </summary>
    private void UpdateStaticRocks()
    {
        HashSet<Vector3I> neededNodes = new();

        // Spawn or update rocks for near nodes
        foreach (var node in _nearNodes)
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
                    if (meshInstance != null)
                    {
                        int materialIndex = (int)node.Material;
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

        _staticRockCount = _staticRocks.Count;
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
            int materialIndex = (int)node.Material;
            color = Materials.materialColors[materialIndex];

            _multiMeshRock.Multimesh.SetInstanceColor(i, color);
        }

        _multiMeshCount = count;
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
        text += $"Static: {_staticRockCount} / {_maxStaticRocks}\n";
        text += $"Rigid: 0\n";
        text += $"Culling: {(_neighborCullingEnabled ? "ON" : "OFF")}\n";
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
            //_octree.EnableCrossSectionCut = enable;
            _needsMultiMeshUpdate = true;
            UpdateLOD();
        }
    }

    /// <summary>
    /// Gets the underlying voxel octree.
    /// </summary>
    public Octree GetOctree()
    {
        return _octree;
    }
}
