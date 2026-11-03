using Godot;
using System.Collections.Generic;

/// <summary>Moves the scene-authored water strips; re-rolls only after they leave the viewport.</summary>
public partial class WaterDrift : Control
{
	[Export] public Godot.Collections.Array<Texture2D> Variants { get; set; } = new();
	[Export] public Vector2 WidthRange { get; set; } = new(0.22f, 0.42f);
	[Export] public Vector2 SpeedRange { get; set; } = new(0.014f, 0.028f);
	[Export] public Vector2 OpacityRange { get; set; } = new(0.16f, 0.30f);
	[Export] public Color LightTint { get; set; } = Colors.White;
	[Export] public Color DarkTint { get; set; } = new(0.48f, 0.58f, 0.62f);

	private readonly RandomNumberGenerator _random = new();
	private readonly List<TextureRect> _strips = new();
	private readonly List<float> _speeds = new();
	private Vector2 _previousSize;
	private bool _paused;

	public override void _Ready()
	{
		_random.Randomize();
		foreach (Node child in GetChildren())
		{
			if (child is TextureRect strip)
			{
				_strips.Add(strip);
				_speeds.Add(0);
			}
		}
		_previousSize = Size;
		for (int i = 0; i < _strips.Count; i++) Recycle(i, true);
		Resized += OnResized;
	}

	public void SetPaused(bool paused) => _paused = paused;

	public override void _Process(double delta)
	{
		if (_paused || !IsVisibleInTree() || Size.X <= 0) return;
		for (int i = 0; i < _strips.Count; i++)
		{
			TextureRect strip = _strips[i];
			strip.Position += new Vector2(_speeds[i] * Size.X * (float)delta, 0);
			if (strip.Position.X > Size.X + 2) Recycle(i, false);
		}
	}

	private void Recycle(int index, bool initial)
	{
		if (Variants.Count == 0 || Size.X <= 0 || Size.Y <= 0) return;
		TextureRect strip = _strips[index];
		strip.Texture = Variants[_random.RandiRange(0, Variants.Count - 1)];
		float width = Size.X * _random.RandfRange(WidthRange.X, WidthRange.Y);
		Vector2 source = strip.Texture.GetSize();
		strip.Size = new Vector2(width, width * source.Y / Mathf.Max(1, source.X));
		float lane = (index + _random.RandfRange(0.20f, 0.80f)) / _strips.Count;
		strip.Position = new Vector2(
			initial ? _random.RandfRange(-width, Size.X) : -width - _random.RandfRange(8, 48),
			lane * Mathf.Max(0, Size.Y - strip.Size.Y));
		strip.FlipH = _random.Randf() < 0.5f;
		Color tint = index % 2 == 0 ? LightTint : DarkTint;
		tint.A *= _random.RandfRange(OpacityRange.X, OpacityRange.Y);
		strip.Modulate = tint;
		_speeds[index] = _random.RandfRange(SpeedRange.X, SpeedRange.Y);
	}

	private void OnResized()
	{
		if (_previousSize.X > 0 && _previousSize.Y > 0)
		{
			Vector2 ratio = Size / _previousSize;
			foreach (TextureRect strip in _strips)
			{
				strip.Position *= ratio;
				strip.Size *= ratio;
			}
		}
		else
		{
			for (int i = 0; i < _strips.Count; i++) Recycle(i, true);
		}
		_previousSize = Size;
	}
}
