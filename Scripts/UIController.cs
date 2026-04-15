using Godot;
using System;
using System.Collections.Generic;

public class Settings
{
	public Dictionary<string, string> DebugInfo = new Dictionary<string, string>();
	public string DebugText;
	public float RealizationRadius;
	public int MaxStaticRocks;
	public bool NeighborCulling;
	public bool CrossSection;
	public bool SurfaceTraversal;
	
	private static Settings singleton = null;

	public static Settings GetSettings()
	{
		if (singleton == null)
		{
			singleton = new Settings();
		}
		return singleton;
	}
}

public partial class UIController : Control
{
	// Scene nodes
	// These need to be public so other scripts can connect to signals from the UI elements
	[Export] public RichTextLabel DebugLabel;
	[Export] public Button DumpDebugButton;
	[Export] public HSlider RealizationRadiusSlider;
	[Export] public Label RealizationRadiusLabel;
	[Export] public HSlider MaxStaticRocksSlider;
	[Export] public Label MaxStaticRocksLabel;
	[Export] public CheckButton NeighborCullingCheck;
	[Export] public CheckButton CrossSectionCheck;
	[Export] public Button ConsolidateButton;
	[Export] public CheckButton SurfaceTraversalCheck;

	private Settings _settings = Settings.GetSettings();

	private static UIController singleton = null;

	public static UIController GetUIController()
	{
		if (singleton == null)
		{
			throw new Exception("UIController singleton not initialized yet");
		}
		return singleton;
	}

	// Handle signals
	private void OnDumpDebug()
	{
		GD.Print(_settings.DebugText);
	}
	private void OnRealizationRadiusChanged(double value)
	{
		_settings.RealizationRadius = (float) value;
		RealizationRadiusLabel.Text = $"Realization Radius: {value:F0}m";
	}

	private void OnMaxStaticRocksChanged(double value)
	{
		_settings.MaxStaticRocks = (int) value;
		MaxStaticRocksLabel.Text = $"Max Static Rocks: {_settings.MaxStaticRocks}";
	}

	private void OnNeighborCullingChanged(bool value)
	{
		_settings.NeighborCulling = value;
	}

	private void OnCrossSectionChanged(bool value)
	{
		_settings.CrossSection = value;
	}

	private void OnSurfaceTraversalChanged(bool value)
	{
		_settings.SurfaceTraversal = value;
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		singleton = this;

		// Connect signals
		DumpDebugButton.Pressed += OnDumpDebug;
		RealizationRadiusSlider.ValueChanged += OnRealizationRadiusChanged;
		MaxStaticRocksSlider.ValueChanged += OnMaxStaticRocksChanged;
		NeighborCullingCheck.Toggled += OnNeighborCullingChanged;
		CrossSectionCheck.Toggled += OnCrossSectionChanged;
		//ConsolidateButton.Pressed += OnConsolidate; // UIController does not need to connect to this
		SurfaceTraversalCheck.Toggled += OnSurfaceTraversalChanged;

		// Fill in initial values
		OnRealizationRadiusChanged(RealizationRadiusSlider.Value);
		OnMaxStaticRocksChanged(MaxStaticRocksSlider.Value);
		OnNeighborCullingChanged(NeighborCullingCheck.ButtonPressed);
		OnCrossSectionChanged(CrossSectionCheck.ButtonPressed);
		OnSurfaceTraversalChanged(SurfaceTraversalCheck.ButtonPressed);
	}

	private void GenerateDebugText()
	{
		Dictionary<string, string> debugInfo = _settings.DebugInfo;
		List<string> lines = new List<string>();

		// FPS and camera
		lines.Add($"FPS: {Engine.GetFramesPerSecond()}");
		if (debugInfo.TryGetValue("CameraPos", out string cameraPos)) lines.Add($"Camera: {cameraPos}");
		if (debugInfo.TryGetValue("CameraDist", out string cameraDist)) lines.Add($"Distance: {cameraDist}");
		lines.Add("---");

		// Timing info
		if (debugInfo.TryGetValue("OctreeTime", out string octreeTime)) lines.Add($"Octree: {octreeTime}");
		if (debugInfo.TryGetValue("MeshTime", out string meshTime)) lines.Add($"Meshes: {meshTime}");
		// Engine.GetLastRenderTime()?
		lines.Add($"Render: ??? ms");
		lines.Add("---");

		// Traversal
		if (debugInfo.TryGetValue("TraversalLines", out string traversalLines)) lines.Add(traversalLines); // Octree.cs will format this multiline string
		lines.Add("---");

		// Meshes
		if (debugInfo.TryGetValue("MultiMeshCount", out string multiMeshCount)) lines.Add($"MultiMesh: {multiMeshCount}");
		if (debugInfo.TryGetValue("StaticCount", out string staticCount)) lines.Add($"Static: {staticCount}");
		if (debugInfo.TryGetValue("RigidCount", out string rigidCount)) lines.Add($"Rigid: {rigidCount}");
		lines.Add("---");

		// Materials
		if (debugInfo.TryGetValue("RockCount", out string rockCount)) lines.Add($"Rock: {rockCount}");
		if (debugInfo.TryGetValue("IceCount", out string iceCount)) lines.Add($"Ice: {iceCount}");
		if (debugInfo.TryGetValue("MetalCount", out string metalCount)) lines.Add($"Metal: {metalCount}");

		_settings.DebugText = string.Join("\n", lines);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	// UI has process_priority = 999 so it runs after everything else
	public override void _Process(double delta)
	{
		GenerateDebugText();
		DebugLabel.Text = _settings.DebugText;
	}
}
