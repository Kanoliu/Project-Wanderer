using Godot;
using System;

public partial class FishingPrototype : Node3D
{
	private enum RodState { Ready, Casting, Floating, Reeling }

	private const float ReadyPitch = 0.02f;
	private const float BackSwingPitch = 0.48f;
	private const float CastPitch = -0.34f;
	private const float CastDuration = 1.0f;
	private const float ReelSpeed = 4.6f;

	private readonly Vector3[] _rodJoints = new Vector3[5];
	private Node3D _rodRig = null!;
	private MeshInstance3D[] _rodSegments = Array.Empty<MeshInstance3D>();
	private MeshInstance3D _fishingLine = null!;
	private Node3D _bobber = null!;
	private MeshInstance3D[] _ripples = Array.Empty<MeshInstance3D>();
	private Label _statusLabel = null!;
	private Label _distanceLabel = null!;
	private RodState _state = RodState.Ready;
	private Vector3 _bobberPosition;
	private Vector3 _castStart;
	private Vector3 _castTarget;
	private float _castElapsed;
	private float _elapsed;
	private bool _rightMouseHeld;

	public override void _Ready()
	{
		_rodRig = GetNode<Node3D>("RodRig");
		_rodSegments = new MeshInstance3D[]
		{
			GetNode<MeshInstance3D>("RodRig/Grip"),
			GetNode<MeshInstance3D>("RodRig/RodLower"),
			GetNode<MeshInstance3D>("RodRig/RodMiddle"),
			GetNode<MeshInstance3D>("RodRig/RodTip")
		};
		_fishingLine = GetNode<MeshInstance3D>("FishingLine");
		_bobber = GetNode<Node3D>("Bobber");
		_ripples = new MeshInstance3D[]
		{
			GetNode<MeshInstance3D>("Ripple1"),
			GetNode<MeshInstance3D>("Ripple2"),
			GetNode<MeshInstance3D>("Ripple3")
		};
		_statusLabel = GetNode<Label>("Interface/StatusPanel/StatusRows/Status");
		_distanceLabel = GetNode<Label>("Interface/StatusPanel/StatusRows/Distance");

		_rodRig.Rotation = new Vector3(ReadyPitch, 0.08f, -0.10f);
		UpdateRodGeometry(0.0f, 0.0f);
		_bobberPosition = GetRodTip();
		UpdateDynamicMeshes();
		UpdateInterface("准备抛竿");
	}

	public override void _Process(double delta)
	{
		float frameDelta = (float)delta;
		_elapsed += frameDelta;

		switch (_state)
		{
			case RodState.Casting:
				UpdateCast(frameDelta);
				break;
			case RodState.Floating:
				_bobberPosition.Y = 0.10f + Mathf.Sin(_elapsed * 2.7f) * 0.055f;
				if (IsReelHeld())
				{
					_state = RodState.Reeling;
					UpdateInterface("收线中 · 鱼竿受力弯曲");
				}
				break;
			case RodState.Reeling:
				UpdateReel(frameDelta);
				break;
		}

		float tension = _state == RodState.Reeling ? 1.0f : 0.0f;
		float castFlex = _state == RodState.Casting
			? Mathf.Sin(Mathf.Clamp(_castElapsed / CastDuration, 0.0f, 1.0f) * Mathf.Pi)
			: 0.0f;
		UpdateRodGeometry(tension, castFlex);
		UpdateDynamicMeshes();
		UpdateDistance();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
			{
				TryCast();
				GetViewport().SetInputAsHandled();
			}
			else if (mouse.ButtonIndex == MouseButton.Right)
			{
				_rightMouseHeld = mouse.Pressed;
				GetViewport().SetInputAsHandled();
			}
		}

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Space)
			{
				TryCast();
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.Escape)
			{
				ResetRod();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void TryCast()
	{
		if (_state != RodState.Ready) return;
		_state = RodState.Casting;
		_castElapsed = 0.0f;
		_castStart = GetRodTip();
		_castTarget = new Vector3(Mathf.Sin(_elapsed * 0.73f) * 1.25f, 0.10f, -8.2f);
		_bobberPosition = _castStart;
		UpdateInterface("蓄力挥杆");
	}

	private void UpdateCast(float delta)
	{
		_castElapsed += delta;
		float normalized = Mathf.Clamp(_castElapsed / CastDuration, 0.0f, 1.0f);
		if (normalized < 0.24f)
		{
			float windUp = SmoothStep(normalized / 0.24f);
			SetRodPitch(Mathf.Lerp(ReadyPitch, BackSwingPitch, windUp));
			_castStart = GetRodTip();
			_bobberPosition = _castStart;
			return;
		}

		float flight = SmoothStep((normalized - 0.24f) / 0.76f);
		SetRodPitch(Mathf.Lerp(BackSwingPitch, CastPitch, flight));
		_bobberPosition = _castStart.Lerp(_castTarget, flight);
		_bobberPosition.Y += Mathf.Sin(flight * Mathf.Pi) * 3.0f;
		if (normalized >= 1.0f)
		{
			_state = RodState.Floating;
			SetRodPitch(ReadyPitch - 0.08f);
			_bobberPosition = _castTarget;
			UpdateInterface("浮标入水 · 按住右键 / R 收线");
		}
	}

	private void UpdateReel(float delta)
	{
		if (!IsReelHeld())
		{
			_state = RodState.Floating;
			UpdateInterface("暂停收线");
			return;
		}

		Vector3 rodTip = GetRodTip();
		_bobberPosition = _bobberPosition.MoveToward(rodTip, ReelSpeed * delta);
		_bobberPosition.Y += Mathf.Sin(_elapsed * 8.0f) * 0.004f;
		if (_bobberPosition.DistanceTo(rodTip) < 0.28f) ResetRod();
	}

	private void UpdateRodGeometry(float tension, float castFlex)
	{
		_rodJoints[0] = Vector3.Zero;
		_rodJoints[1] = new Vector3(-0.02f, 0.22f, -0.64f);
		_rodJoints[2] = new Vector3(-0.05f, 0.50f, -1.45f);
		_rodJoints[3] = new Vector3(-0.10f + tension * 0.14f, 0.77f - tension * 0.08f, -2.25f);
		_rodJoints[4] = new Vector3(-0.17f + tension * 0.42f, 0.98f - tension * 0.25f + castFlex * 0.10f, -3.02f);

		for (int index = 0; index < _rodSegments.Length; index++)
			PlaceCylinder(_rodSegments[index], _rodJoints[index], _rodJoints[index + 1]);
	}

	private void UpdateDynamicMeshes()
	{
		bool lineVisible = _state != RodState.Ready;
		_fishingLine.Visible = lineVisible;
		_bobber.Visible = lineVisible;
		if (lineVisible)
		{
			PlaceCylinder(_fishingLine, GetRodTip(), _bobberPosition);
			_bobber.Position = _bobberPosition;
		}

		bool waterContact = _state == RodState.Floating || _state == RodState.Reeling;
		for (int index = 0; index < _ripples.Length; index++)
		{
			MeshInstance3D ripple = _ripples[index];
			ripple.Visible = waterContact;
			if (!waterContact) continue;
			float phase = Mathf.PosMod(_elapsed * 0.48f + index / 3.0f, 1.0f);
			ripple.Position = new Vector3(_bobberPosition.X, 0.035f, _bobberPosition.Z);
			ripple.Scale = new Vector3(0.45f + phase * 2.1f, 1.0f, 0.45f + phase * 2.1f);
			ripple.Transparency = phase;
		}
	}

	private static void PlaceCylinder(Node3D node, Vector3 from, Vector3 to)
	{
		Vector3 direction = to - from;
		float length = Mathf.Max(direction.Length(), 0.001f);
		node.Position = (from + to) * 0.5f;
		node.Quaternion = new Quaternion(Vector3.Up, direction / length);
		node.Scale = new Vector3(1.0f, length, 1.0f);
	}

	private Vector3 GetRodTip() => _rodRig.ToGlobal(_rodJoints[^1]);

	private void SetRodPitch(float pitch)
	{
		_rodRig.Rotation = new Vector3(pitch, 0.08f, -0.10f);
	}

	private void ResetRod()
	{
		_state = RodState.Ready;
		SetRodPitch(ReadyPitch);
		_rightMouseHeld = false;
		UpdateRodGeometry(0.0f, 0.0f);
		_bobberPosition = GetRodTip();
		UpdateDynamicMeshes();
		UpdateInterface("准备再次抛竿");
	}

	private bool IsReelHeld() => _rightMouseHeld || Input.IsKeyPressed(Key.R);
	private void UpdateInterface(string status) => _statusLabel.Text = status;

	private void UpdateDistance()
	{
		float distance = _state == RodState.Ready ? 0.0f : _bobberPosition.DistanceTo(GetRodTip());
		_distanceLabel.Text = $"线距 {distance:0.0} m";
	}

	private static float SmoothStep(float value)
	{
		value = Mathf.Clamp(value, 0.0f, 1.0f);
		return value * value * (3.0f - 2.0f * value);
	}
}
