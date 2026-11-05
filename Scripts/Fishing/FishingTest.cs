using Godot;
using System.Collections.Generic;

/// <summary>Direct movement testing: no bait selection, random fish draw or bite gate.</summary>
public partial class FishingTest : Control
{
    private static readonly (string Id, string Name)[] Presets = {
        ("fight.stable", "01 · 温和 / Gentle"),
        ("fight.active", "02 · 活跃 / Active"),
        ("fight.sprint", "03 · 冲刺 / Sprint")
    };

    private readonly List<FightProfileDefinition> _profiles = new();
    private Control _setup;
    private OptionButton _presetPicker;
    private SpinBox _seedPicker;
    private Label _parameters;
    private Label _status;
    private Button _start;
    private Button _retry;
    private Button _back;
    private FightController _fight;
    private FishMovementSettings _lastSettings;
    private string _lastName;
    private int _lastSeed;
    private double _elapsed;
    private bool _running;

    public override void _Ready()
    {
        _setup = GetNode<Control>("Page/Stack/SetupPanel");
        _presetPicker = GetNode<OptionButton>("Page/Stack/SetupPanel/Margin/Options/PresetPicker");
        _seedPicker = GetNode<SpinBox>("Page/Stack/SetupPanel/Margin/Options/SeedRow/SeedPicker");
        _parameters = GetNode<Label>("Page/Stack/SetupPanel/Margin/Options/Parameters");
        _start = GetNode<Button>("Page/Stack/SetupPanel/Margin/Options/StartButton");
        _status = GetNode<Label>("Page/Stack/Status");
        _fight = GetNode<FightController>("Page/Stack/FightController");
        _retry = GetNode<Button>("Page/Stack/Actions/RetryButton");
        _back = GetNode<Button>("Page/Stack/Actions/BackButton");

        _presetPicker.ItemSelected += OnPresetSelected;
        _start.Pressed += StartSelected;
        _retry.Pressed += Retry;
        _back.Pressed += ShowSetup;
        _fight.FightResolved += OnFightResolved;

        var data = GetNode<FishingDataStore>("/root/FishingDataStore");
        foreach (var preset in Presets)
        {
            if (data.IsLoaded && data.TryGetFightProfile(preset.Id, out var profile))
            {
                _profiles.Add(profile);
                _presetPicker.AddItem(preset.Name);
            }
        }
        if (_profiles.Count != Presets.Length)
        {
            _start.Disabled = true;
            _presetPicker.Disabled = true;
            _status.Text = "测试配置加载失败：需要 fight.stable、fight.active 和 fight.sprint。请检查 Output。";
            return;
        }
        _presetPicker.Select(0);
        OnPresetSelected(0);
        ShowSetup();
    }

    public override void _Process(double delta)
    {
        if (!_running) return;
        _elapsed += delta;
        string tracking = _fight.FishInsideBox ? "框内 · 绿色" : "框外 · 珊瑚红";
        string visibility = _fight.FishIsDiving ? "潜水中：继续预测鱼的位置" : "鱼可见";
        _status.Text = $"{_lastName}  |  {_elapsed:0.0}s  |  {tracking}  |  {visibility}  |  Seed {_lastSeed}";
    }

    private void OnPresetSelected(long index)
    {
        if (index < 0 || index >= _profiles.Count) return;
        var m = _profiles[(int)index].Movement;
        _parameters.Text =
            $"巡游速度 {m.CruiseSpeed:0.00}    速度变化 ±{m.SpeedVariation:P0}    加速度 {m.Acceleration:0.00}\n" +
            $"转向 {m.MaxTurnRate:0}°/s    决策间隔 {m.SteeringInterval:0.00}s    游移角度 ±{m.WanderAngle:0}°    避边距离 {m.EdgeAvoidanceDistance:P0}\n" +
            $"冲刺速度 ×{m.BurstSpeedMultiplier:0.0}    持续 {m.BurstDuration:0.00}s    间隔 {m.BurstInterval:0.0}s    冲刺转向 {m.BurstTurnRate:0}°/s\n" +
            $"潜水频率 {m.DiveRate:0.0}/min    潜水时长 {m.DiveDuration:0.0}s\n" +
            $"避框强度 {m.AvoidanceStrength:0.00}    反应延迟 {m.ReactionDelay:0.00}s    逆向冲刺转向概率 {m.BurstCounterTurnChance:P0}";
    }

    private void StartSelected()
    {
        int index = _presetPicker.Selected;
        if (_running || index < 0 || index >= _profiles.Count) return;
        _lastSettings = _profiles[index].Movement.Copy();
        _lastName = _presetPicker.GetItemText(index);
        _lastSeed = (int)_seedPicker.Value;
        BeginFight();
    }

    private void BeginFight()
    {
        _fight.ResetFight();
        _setup.Visible = false;
        _retry.Visible = false;
        _back.Visible = true;
        _back.Text = "停止并选择配置 / Stop & choose preset";
        _fight.Visible = true;
        _elapsed = 0;
        _running = true;
        // Let containers lay out the newly visible panel before reading its size.
        CallDeferred(nameof(StartLaidOutFight));
    }

    private void StartLaidOutFight()
    {
        if (_running) _fight.StartFight(_lastSettings, _lastSeed);
    }

    private void Retry()
    {
        if (!_running && _lastSettings != null) BeginFight();
    }

    private void OnFightResolved(bool caught)
    {
        _running = false;
        _status.Text = $"{(caught ? "钓获成功" : "鱼已逃脱")}  |  {_lastName}  |  {_elapsed:0.0}s  |  Seed {_lastSeed}";
        _retry.Visible = true;
        _back.Text = "选择其他配置 / Choose preset";
        _retry.GrabFocus();
    }

    private void ShowSetup()
    {
        _running = false;
        _fight.ResetFight();
        _fight.Visible = false;
        _setup.Visible = true;
        _retry.Visible = false;
        _back.Visible = false;
        _status.Text = "选择参数配置后直接开始搏鱼。绿色 = 框内；珊瑚红 = 框外，潜水时同样有效。";
        _start.GrabFocus();
    }
}
