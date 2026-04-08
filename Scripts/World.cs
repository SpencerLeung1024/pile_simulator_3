using Godot;
using System;

public partial class World : Node3D
{
	// Scene nodes
	[Export] private Camera3D _camera;
	[Export] private Asteroid _asteroid;

	// UI references
	private HSlider _realDistanceSlider;
	private Label _sliderLabel;
	private CheckButton _crossSectionCheck;
	private HSlider _maxStaticRocksSlider;
	private Label _maxRocksLabel;
	private CheckButton _neighborCullingCheck;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Get UI references
		var ui = GetNodeOrNull<Control>("UI");
		if (ui != null)
		{
			_realDistanceSlider = ui.GetNodeOrNull<HSlider>("RealDistanceSlider");
			_sliderLabel = ui.GetNodeOrNull<Label>("SliderLabel");
			_crossSectionCheck = ui.GetNodeOrNull<CheckButton>("CrossSectionCheck");
			_maxStaticRocksSlider = ui.GetNodeOrNull<HSlider>("MaxStaticRocksSlider");
			_maxRocksLabel = ui.GetNodeOrNull<Label>("MaxRocksLabel");
			_neighborCullingCheck = ui.GetNodeOrNull<CheckButton>("NeighborCullingCheck");

			// Connect realization radius slider
			if (_realDistanceSlider != null)
			{
				_realDistanceSlider.ValueChanged += OnRealDistanceSliderChanged;
				UpdateSliderLabel(_realDistanceSlider.Value);
			}

			// Connect cross-section toggle
			if (_crossSectionCheck != null)
			{
				_crossSectionCheck.Toggled += OnCrossSectionToggled;
			}

			// Connect max static rocks slider
			if (_maxStaticRocksSlider != null)
			{
				_maxStaticRocksSlider.ValueChanged += OnMaxStaticRocksSliderChanged;
				UpdateMaxRocksLabel(_maxStaticRocksSlider.Value);
			}

			// Connect neighbor culling toggle
			if (_neighborCullingCheck != null)
			{
				_neighborCullingCheck.Toggled += OnNeighborCullingToggled;
			}
		}
	}

	private void OnRealDistanceSliderChanged(double value)
	{
		if (_asteroid != null)
		{
			_asteroid.SetRealizationRadius((float)value);
		}
		UpdateSliderLabel(value);
	}

	private void UpdateSliderLabel(double value)
	{
		if (_sliderLabel != null)
		{
			_sliderLabel.Text = $"Realization Radius: {value:F0}m";
		}
	}

	private void OnCrossSectionToggled(bool enabled)
	{
		if (_asteroid != null)
		{
			_asteroid.SetCrossSectionCut(enabled);
		}
		GD.Print($"Cross-section cut: {(enabled ? "enabled" : "disabled")}");
	}

	private void OnMaxStaticRocksSliderChanged(double value)
	{
		if (_asteroid != null)
		{
			// Note: The asteroid script handles this via direct UI connection,
			// but we update the label here
		}
		UpdateMaxRocksLabel(value);
	}

	private void UpdateMaxRocksLabel(double value)
	{
		if (_maxRocksLabel != null)
		{
			_maxRocksLabel.Text = $"Max Static Rocks: {value:F0}";
		}
	}

	private void OnNeighborCullingToggled(bool enabled)
	{
		if (_asteroid != null)
		{
			// Note: The asteroid script handles this via direct UI connection
		}
		GD.Print($"Neighbor culling: {(enabled ? "enabled" : "disabled")}");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
