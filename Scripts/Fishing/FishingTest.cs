using Godot;
using System.Collections.Generic;

/// <summary>Direct movement testing: no bait selection, random fish draw or bite gate.</summary>
public partial class FishingTest : Control
{
    private readonly List<FishDefinition> _fish = new();
    private FishingDataStore _data;
    private Control _setup;
    private OptionButton _fishPicker;
    private CheckBox _specialVariantToggle;
    private SpinBox _seedPicker;
    private CheckBox _fixedSeedToggle;
    private Label _parameters;
    private Label _status;
    private Button _start;
    private Button _retry;
    private Button _back;
    private FightController _fight;
    private FishMovementSettings _lastSettings;
    private float _lastGainRate;
    private float _lastLossRate;
    private string _lastName;
    private int _lastSeed;
    private double _elapsed;
    private bool _running;

    public override void _Ready()
    {
        _setup = GetNode<Control>("Page/Stack/SetupPanel");
        _fishPicker = GetNode<OptionButton>("Page/Stack/SetupPanel/Margin/Options/FishPicker");
        _specialVariantToggle = GetNode<CheckBox>("Page/Stack/SetupPanel/Margin/Options/SpecialVariantToggle");
        _seedPicker = GetNode<SpinBox>("Page/Stack/SetupPanel/Margin/Options/SeedRow/SeedPicker");
        _fixedSeedToggle = GetNode<CheckBox>("Page/Stack/SetupPanel/Margin/Options/SeedRow/FixedSeedToggle");
        _fixedSeedToggle.Toggled += enabled => _seedPicker.Editable = enabled;
        _parameters = GetNode<Label>("Page/Stack/SetupPanel/Margin/Options/Parameters");
        _start = GetNode<Button>("Page/Stack/SetupPanel/Margin/Options/StartButton");
        _status = GetNode<Label>("Page/Stack/Status");
        _fight = GetNode<FightController>("Page/Stack/FightController");
        _retry = GetNode<Button>("Page/Stack/Actions/RetryButton");
        _back = GetNode<Button>("Page/Stack/Actions/BackButton");

        _fishPicker.ItemSelected += OnFishSelected;
        _specialVariantToggle.Toggled += _ => OnFishSelected(_fishPicker.Selected);
        _start.Pressed += StartSelected;
        _retry.Pressed += Retry;
        _back.Pressed += ShowSetup;
        _fight.FightResolved += OnFightResolved;

        _data = GetNode<FishingDataStore>("/root/FishingDataStore");
        if (_data.IsLoaded)
        {
            foreach (var fish in _data.Fish)
            {
                _fish.Add(fish);
                _fishPicker.AddItem($"{fish.DisplayName} · {DifficultyName(fish.CatchDifficulty)}");
            }
        }
        if (_fish.Count == 0)
        {
            _start.Disabled = true;
            _fishPicker.Disabled = true;
            _specialVariantToggle.Disabled = true;
            _status.Text = "鱼种配置加载失败，请检查 Output。";
            return;
        }
        _fishPicker.Select(0);
        OnFishSelected(0);
        ShowSetup();
    }

    public override void _Process(double delta)
    {
        if (!_running) return;
        _elapsed += delta;
        string tracking = _fight.FishInsideBox ? "框内 · 已跟住" : "框外 · 调整位置";
        string visibility = _fight.FishIsDiving ? "潜水中：继续预测鱼的位置" : "鱼可见";
        _status.Text = $"{_lastName}  |  {_elapsed:0.0}s  |  {tracking}  |  {visibility}  |  Seed {_lastSeed}";
    }

    private void OnFishSelected(long index)
    {
        if (index < 0 || index >= _fish.Count) return;
        var fish = _fish[(int)index];
        var p = _data.CreateFightProfile(fish.Id, _specialVariantToggle.ButtonPressed);
        var m = p.Movement;
        _data.TryGetBiteProfile(fish.BiteProfileId, out var bite);
        string form = _specialVariantToggle.ButtonPressed
            ? $"特殊个体 · 统一强度 ×{_data.SpecialVariantMultiplier:0.00}" : "普通个体";
        _parameters.Text =
            $"{form}    咬饵方式：{BiteName(bite.Pattern)}\n" +
            $"{p.Description}\n" +
            $"巡游速度 {m.CruiseSpeed:0.00}    速度变化 ±{m.SpeedVariation:P0}    加速度 {m.Acceleration:0.00}\n" +
            $"转向 {m.MaxTurnRate:0}°/s    决策间隔 {m.SteeringInterval:0.00}s    游移角度 ±{m.WanderAngle:0}°    避边距离 {m.EdgeAvoidanceDistance:P0}\n" +
            $"冲刺速度 ×{m.BurstSpeedMultiplier:0.##}    持续 {m.BurstDuration:0.00}s    间隔 {m.BurstInterval:0.0}s    冲刺转向 {m.BurstTurnRate:0}°/s\n" +
            $"潜水频率 {m.DiveRate:0.0}/min    潜水时长 {m.DiveDuration:0.0}s    最短可见 {m.DiveMinDelay:0.0}s\n" +
            $"横向偏好 {m.HorizontalBias:P0}    折返概率 {m.ReversalChance:P0}    停游 {m.PauseDuration:0.0}s / {m.PauseInterval:0.0}s\n" +
            $"避框强度 {m.AvoidanceStrength:0.00}    反应延迟 {m.ReactionDelay:0.00}s    逆向冲刺转向概率 {m.BurstCounterTurnChance:P0}\n" +
            $"框内 +{p.GainRate:P1}/s    框外 −{p.LossRate:P1}/s（{LossDifficultyName(p.LossDifficulty.Value)}）    理想收鱼 {0.65f / p.GainRate:0.0}s";
    }

    private void StartSelected()
    {
        int index = _fishPicker.Selected;
        if (_running || index < 0 || index >= _fish.Count) return;
        var p = _data.CreateFightProfile(_fish[index].Id, _specialVariantToggle.ButtonPressed);
        _lastSettings = p.Movement;
        _lastGainRate = p.GainRate;
        _lastLossRate = p.LossRate;
        _lastName = _fishPicker.GetItemText(index) + (_specialVariantToggle.ButtonPressed ? " · 特殊个体" : "");
        if (!_fixedSeedToggle.ButtonPressed) _seedPicker.Value = System.Random.Shared.Next();
        _lastSeed = (int)_seedPicker.Value;
        BeginFight();
    }

    private void BeginFight()
    {
        _fight.ResetFight();
        _setup.Visible = false;
        _retry.Visible = false;
        _back.Visible = false;
		GetNode<Control>("Page/Stack/Title").Visible = false;
		GetNode<Control>("Page/Stack/Instructions").Visible = false;
		_status.Visible = false;
        _back.Text = "停止并选择鱼种";
        _fight.Visible = true;
        _elapsed = 0;
        _running = true;
        // Let containers lay out the newly visible panel before reading its size.
        CallDeferred(nameof(StartLaidOutFight));
    }

    private void StartLaidOutFight()
    {
        if (_running) _fight.StartFight(_lastSettings, _lastSeed, _lastGainRate, _lastLossRate);
    }

    private void Retry()
    {
        if (!_running && _lastSettings != null) BeginFight();
    }

    private void OnFightResolved(bool caught)
    {
        _running = false;
        _status.Text = $"{(caught ? "钓获成功" : "鱼已逃脱")}  |  {_lastName}  |  {_elapsed:0.0}s  |  Seed {_lastSeed}";
        _status.Visible = true;
        _back.Visible = true;
        _retry.Visible = true;
        _back.Text = "选择其他鱼种";
        _retry.GrabFocus();
    }

    private void ShowSetup()
    {
		GetNode<Control>("Page/Stack/Title").Visible = true;
		GetNode<Control>("Page/Stack/Instructions").Visible = true;
		_status.Visible = true;
        _running = false;
        _fight.ResetFight();
        _fight.Visible = false;
        _setup.Visible = true;
        _retry.Visible = false;
        _back.Visible = false;
        _status.Text = "选择鱼种后直接开始搏鱼。勾选特殊个体可测试统一强化后的手感。";
        _start.GrabFocus();
    }

    private static string BiteName(FishBitePattern pattern) => pattern switch
    {
        FishBitePattern.Probe => "试探型",
        FishBitePattern.Strike => "猛咬型",
        _ => "拖饵型"
    };

    private static string DifficultyName(FishCatchDifficulty difficulty) => difficulty switch
    {
        FishCatchDifficulty.Low => "低",
        FishCatchDifficulty.Medium => "中",
        _ => "高"
    };

    private static string LossDifficultyName(FishCatchDifficulty difficulty) => difficulty switch
    {
        FishCatchDifficulty.Low => "简单掉落",
        FishCatchDifficulty.Medium => "中等掉落",
        _ => "困难掉落"
    };
}
