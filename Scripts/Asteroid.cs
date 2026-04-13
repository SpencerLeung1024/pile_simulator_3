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
    [Export] private float _radius;
    [Export] private ulong _seed;
    [Export] private float _cameraMoveThreshold; // Minimum camera movement to trigger update

    // Internal state
    private Octree _octree;
    private AsteroidGenerator _generator;

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
    private int _octreeUs = 0;
    private int _meshUs = 0;

    private int _multiMeshCount = 0;
    private int _staticRockCount = 0;

    private int _rockCount = 0;
    private int _iceCount = 0;
    private int _metalCount = 0;

    // Settings and UI singletons
    private Settings _settings;
    private UIController _uiController;
    private FreeCamController _camera;

    // Handle signals
    private void OnRealizationRadiusChanged(double value)
    {
        _needsMultiMeshUpdate = true;
    }

    private void OnMaxStaticRocksChanged(double value)
    {
        _needsMultiMeshUpdate = true;
    }

    private void OnNeighborCullingChanged(bool value)
    {
        _needsMultiMeshUpdate = true;
    }

    private void OnCrossSectionChanged(bool value)
    {
        _needsMultiMeshUpdate = true;
    }

    private void OnConsolidate()
    {
        _octree?.Consolidate(deleteChildren: true);
        _needsMultiMeshUpdate = true;
    }

    private void UpdateRockCounts()
    {
        if (_rockCount + _iceCount + _metalCount == _nearNodes.Count) return; // No change in near nodes, skip counting

        _rockCount = 0;
        _iceCount = 0;
        _metalCount = 0;

        foreach (var node in _nearNodes)
        {
            switch (node.Material)
            {
                case MaterialEnum.Rock:
                    _rockCount++;
                    break;
                case MaterialEnum.Ice:
                    _iceCount++;
                    break;
                case MaterialEnum.Metal:
                    _metalCount++;
                    break;
            }
        }
    }

    private void PushDebugInfo()
    {
        Dictionary<string, string> debugInfo = _settings.DebugInfo;

        debugInfo["OctreeTime"] = $"{_octreeUs/1000.0f:F2} ms";
        debugInfo["MeshTime"] = $"{_meshUs/1000.0f:F2} ms";

        debugInfo["MultiMeshCount"] = _multiMeshCount.ToString();
        debugInfo["StaticCount"] = $"{_staticRockCount} / {_settings.MaxStaticRocks}";
        debugInfo["RigidCount"] = "0";

        // Count materials in near nodes
        // May return if there has been no change
        UpdateRockCounts();

        debugInfo["RockCount"] = _rockCount.ToString();
        debugInfo["IceCount"] = _iceCount.ToString();
        debugInfo["MetalCount"] = _metalCount.ToString();
    }

    public override void _Ready()
    {
        float bulkDensity = 2.5f;
        float idealVolume = (4.0f / 3.0f) * Mathf.Pi * Mathf.Pow(_radius, 3);
        float gravitationalMass = bulkDensity * idealVolume;
        float maxHeight = _radius * 0.8f;

        // Initialize asteroid generator
        _generator = new AsteroidGenerator(_seed, _radius, gravitationalMass, maxHeight);

        // Initialize the octree with procedural generation
        _octree = new Octree(_generator);

        // Get singletons
        _settings = Settings.GetSettings();
        _uiController = UIController.GetUIController();
        _camera = FreeCamController.GetFreeCamController();
        // Connect to UI signals through UIController
        if (_uiController != null)
        {
            _uiController.RealizationRadiusSlider.ValueChanged += OnRealizationRadiusChanged;
            _uiController.MaxStaticRocksSlider.ValueChanged += OnMaxStaticRocksChanged;
            _uiController.NeighborCullingCheck.Toggled += OnNeighborCullingChanged;
            _uiController.CrossSectionCheck.Toggled += OnCrossSectionChanged;
            _uiController.ConsolidateButton.Pressed += OnConsolidate;
        }

        // Initial update
        UpdateLOD();
        PushDebugInfo();
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

        PushDebugInfo();
    }

    /// <summary>
    /// Main LOD update logic using Barnes-Hut style theta-based approximation.
    /// Uses GetVisibleNodes which renders larger blocks for distant areas (O(log d) instead of O(d²)).
    /// </summary>
    private void UpdateLOD()
    {
        if (_camera == null || _octree == null) return;

        Vector3 cameraPos = _camera.GlobalPosition;
        float thetaThreshold = 1.0f / _settings.RealizationRadius; // I want a 1 voxel to be rendered 50 m away -> 1 / 50 = 0.02

        ulong octreeStartUs = Time.GetTicksUsec();

        List<OctreeNode> visibleNodes = _octree.QueryForLOD(cameraPos, thetaThreshold, _settings.NeighborCulling);

        ulong octreeEndUs = Time.GetTicksUsec();
        _octreeUs = (int)(octreeEndUs - octreeStartUs);

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
        if (_nearNodes.Count > _settings.MaxStaticRocks)
        {
            List<OctreeNode> sortedNearNodes = new List<OctreeNode>(_nearNodes);
            sortedNearNodes.Sort((a, b) =>
            {
                float distA = a.Center.DistanceTo(cameraPos);
                float distB = b.Center.DistanceTo(cameraPos);
                return distA.CompareTo(distB);
            });

            _farNodes.AddRange(sortedNearNodes.GetRange(_settings.MaxStaticRocks, sortedNearNodes.Count - _settings.MaxStaticRocks));
            _nearNodes = sortedNearNodes.GetRange(0, _settings.MaxStaticRocks);
        }

        ulong meshStartUs = Time.GetTicksUsec();

        UpdateStaticRocks();
        UpdateMultiMesh();

        ulong meshEndUs = Time.GetTicksUsec();
        _meshUs = (int)(meshEndUs - meshStartUs);
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
            if (node.Center.Z > 0.0f && _settings.CrossSection) continue; // Skip the closer half of nodes if cross-section cut is enabled

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

        _needsMultiMeshUpdate = false;

        // We need to filter out nodes here so that indices don't get messed up
        if (_settings.CrossSection)
        {
            _farNodes.RemoveAll(node => node.Center.Z > 0.0f);
        }

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

    private void ReturnRockToPool(Node3D rock)
    {
        if (rock != null)
        {
            rock.Visible = false;
            _staticRockPool.Enqueue(rock);
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
