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
	[Export] public HSlider RealizationRadiusSlider;
	[Export] public Label RealizationRadiusLabel;
	[Export] public HSlider MaxStaticRocksSlider;
	[Export] public Label MaxStaticRocksLabel;
	[Export] public CheckButton NeighborCullingCheck;
	[Export] public CheckButton CrossSectionCheck;
	[Export] public Button ConsolidateButton;

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

	private void OnNeighborCullingChanged(bool toggled)
	{
		_settings.NeighborCulling = toggled;
	}

	private void OnCrossSectionChanged(bool toggled)
	{
		_settings.CrossSection = toggled;
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		singleton = this;

		// Connect signals
		RealizationRadiusSlider.ValueChanged += OnRealizationRadiusChanged;
		MaxStaticRocksSlider.ValueChanged += OnMaxStaticRocksChanged;
		NeighborCullingCheck.Toggled += OnNeighborCullingChanged;
		CrossSectionCheck.Toggled += OnCrossSectionChanged;
		//ConsolidateButton.Pressed += OnConsolidate; // UIController does not need to connect to this

		// Fill in initial values
		OnRealizationRadiusChanged(RealizationRadiusSlider.Value);
		OnMaxStaticRocksChanged(MaxStaticRocksSlider.Value);
		OnNeighborCullingChanged(false); // For some reason check buttons have a bug where the first time they're read they're true
		OnCrossSectionChanged(false);
	}

	private void GenerateDebugText()
	{
		Dictionary<string, string> debugInfo = _settings.DebugInfo;
		GD.Print($"{debugInfo["VisitedNodes"]}, {debugInfo["NeighborChecks"]}, {debugInfo["VisibleMeshes"]}");
		GD.Print("UIController");
		GD.Print(Settings.GetSettings());
		GD.Print(debugInfo);
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
		if (debugInfo.TryGetValue("VisitedNodes", out string visitedNodes)) lines.Add($"Visited Nodes: {visitedNodes}");
		if (debugInfo.TryGetValue("NeighborChecks", out string neighborChecks)) lines.Add($"Neighbor Checks: {neighborChecks}");
		if (debugInfo.TryGetValue("VisibleMeshes", out string visibleMeshes)) lines.Add($"Visible Meshes: {visibleMeshes}");
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
