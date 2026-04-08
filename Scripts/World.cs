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

			// Connect slider
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

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
