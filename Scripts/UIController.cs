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
	public bool NeedsConsolidation;
	
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
	[Export] private RichTextLabel _debugLabel;
	[Export] private HSlider _realizationRadiusSlider;
	[Export] private Label _realizationRadiusLabel;
	[Export] private HSlider _maxStaticRocksSlider;
	[Export] private Label _maxStaticRocksLabel;
	[Export] private CheckButton _neighborCullingCheck;
	[Export] private CheckButton _crossSectionCheck;
	[Export] private Button _consolidateButton;

	private Settings settings = Settings.GetSettings();

	// Handle signals
	private void OnRealizationRadiusChanged(double value)
	{
		settings.RealizationRadius = (float) value;
		_realizationRadiusLabel.Text = $"Realization Radius: {value:F0}m";
	}

	private void OnMaxStaticRocksChanged(double value)
	{
		settings.MaxStaticRocks = (int) value;
		_maxStaticRocksLabel.Text = $"Max Static Rocks: {settings.MaxStaticRocks}";
	}

	private void OnNeighborCullingChanged(bool toggled)
	{
		settings.NeighborCulling = toggled;
	}

	private void OnCrossSectionChanged(bool toggled)
	{
		settings.CrossSection = toggled;
	}

	private void OnConsolidate()
	{
		settings.NeedsConsolidation = true;
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Connect signals
		_realizationRadiusSlider.ValueChanged += OnRealizationRadiusChanged;
		_maxStaticRocksSlider.ValueChanged += OnMaxStaticRocksChanged;
		_neighborCullingCheck.Toggled += OnNeighborCullingChanged;
		_crossSectionCheck.Toggled += OnCrossSectionChanged;
		_consolidateButton.Pressed += OnConsolidate;
	}

	private void GenerateDebugText()
	{
		Dictionary<string, string> debugInfo = settings.DebugInfo;
		List<string> lines = new List<string>();

		// FPS and camera
		lines.Add($"FPS: {Engine.GetFramesPerSecond()}");
		if (debugInfo.ContainsKey("CameraPos")) lines.Add(debugInfo["CameraPos"]);
		lines.Add("---");

		// Timing info
		if (debugInfo.ContainsKey("OctreeTime")) lines.Add($"Octree: {debugInfo["OctreeTime"]}");
		if (debugInfo.ContainsKey("MeshTime")) lines.Add($"Meshes: {debugInfo["MeshTime"]}");
		if (debugInfo.ContainsKey("RenderTime")) lines.Add($"Render: {debugInfo["RenderTime"]}");
		lines.Add("---");

		// Traversal
		if (debugInfo.ContainsKey("VisitedNodes")) lines.Add($"Visited Nodes: {debugInfo["VisitedNodes"]}");
		if (debugInfo.ContainsKey("NeighborChecks")) lines.Add($"Neighbor Checks: {debugInfo["NeighborChecks"]}");
		if (debugInfo.ContainsKey("VisibleMeshes")) lines.Add($"Visible Meshes: {debugInfo["VisibleMeshes"]}");
		lines.Add("---");

		// Meshes
		if (debugInfo.ContainsKey("MultiMeshCount")) lines.Add($"MultiMesh: {debugInfo["MultiMeshCount"]}");
		if (debugInfo.ContainsKey("StaticCount")) lines.Add($"Static: {debugInfo["StaticCount"]}");
		if (debugInfo.ContainsKey("RigidCount")) lines.Add($"Rigid: {debugInfo["RigidCount"]}");
		lines.Add("---");

		// Materials
		if (debugInfo.ContainsKey("RockCount")) lines.Add($"Rock: {debugInfo["RockCount"]}");
		if (debugInfo.ContainsKey("IceCount")) lines.Add($"Ice: {debugInfo["IceCount"]}");
		if (debugInfo.ContainsKey("MetalCount")) lines.Add($"Metal: {debugInfo["MetalCount"]}");

		settings.DebugText = string.Join("\n", lines);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	// UI has process_priority = 999 so it runs after everything else
	public override void _Process(double delta)
	{
		GenerateDebugText();
		_debugLabel.Text = settings.DebugText;
	}
}
