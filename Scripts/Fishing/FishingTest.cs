using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FishingTest : Control
{
	private enum FishingState
	{
		Preparation,
		Waiting,
		Bite,
		Fight,
		Result
	}

	private FishingDataStore _dataStore = null!;
	private Label _stateLabel = null!;
	private Label _instructionLabel = null!;
	private Control _setupPanel = null!;
	private OptionButton _baitPicker = null!;
	private OptionButton _depthPicker = null!;
	private Label _candidateLabel = null!;
	private Button _castButton = null!;
	private Label _catchInfoLabel = null!;
	private BiteController _biteController = null!;
	private FightController _fightController = null!;
	private Control _resultPanel = null!;
	private Label _resultLabel = null!;
	private Label _resultReason = null!;
	private Button _restartButton = null!;

	private readonly List<FishingBaitDefinition> _baitOptions = new();
	private readonly List<FishingDepthDefinition> _depthOptions = new();
	private FishingState _state;
	private FishingEventRequest _activeRequest = new();
	private FishDefinition _activeFish = null!;

	public override void _Ready()
	{
		_dataStore = GetNode<FishingDataStore>("/root/FishingDataStore");
		_stateLabel = GetNode<Label>("Page/Stack/StateLabel");
		_instructionLabel = GetNode<Label>("Page/Stack/InstructionLabel");
		_setupPanel = GetNode<Control>("Page/Stack/SetupPanel");
		_baitPicker = GetNode<OptionButton>(
			"Page/Stack/SetupPanel/SetupMargin/SetupStack/BaitRow/BaitPicker"
		);
		_depthPicker = GetNode<OptionButton>(
			"Page/Stack/SetupPanel/SetupMargin/SetupStack/DepthRow/DepthPicker"
		);
		_candidateLabel = GetNode<Label>(
			"Page/Stack/SetupPanel/SetupMargin/SetupStack/CandidateLabel"
		);
		_castButton = GetNode<Button>(
			"Page/Stack/SetupPanel/SetupMargin/SetupStack/CastButton"
		);
		_catchInfoLabel = GetNode<Label>("Page/Stack/CatchInfoLabel");
		_biteController = GetNode<BiteController>("Page/Stack/BiteController");
		_fightController = GetNode<FightController>("Page/Stack/FightController");
		_resultPanel = GetNode<Control>("Page/Stack/ResultPanel");
		_resultLabel = GetNode<Label>(
			"Page/Stack/ResultPanel/ResultStack/ResultLabel"
		);
		_resultReason = GetNode<Label>(
			"Page/Stack/ResultPanel/ResultStack/ResultReason"
		);
		_restartButton = GetNode<Button>(
			"Page/Stack/ResultPanel/ResultStack/RestartButton"
		);

		_baitPicker.ItemSelected += OnSetupChanged;
		_depthPicker.ItemSelected += OnSetupChanged;
		_castButton.Pressed += BeginFishing;
		_biteController.BiteStarted += OnBiteStarted;
		_biteController.BiteResolved += OnBiteResolved;
		_fightController.FightResolved += OnFightResolved;
		_restartButton.Pressed += ShowPreparation;

		PopulateSetupOptions();
		ShowPreparation();
	}

	private void PopulateSetupOptions()
	{
		_baitPicker.Clear();
		_depthPicker.Clear();
		_baitOptions.Clear();
		_depthOptions.Clear();

		if (!_dataStore.IsLoaded)
		{
			_candidateLabel.Text =
				"钓鱼数据加载失败，请检查 Godot Output 中的校验错误。";
			_castButton.Disabled = true;
			return;
		}

		_baitOptions.AddRange(_dataStore.Baits.OrderBy(bait => bait.Id));
		_depthOptions.AddRange(
			_dataStore.Depths.OrderBy(depth => (int)depth.Value)
		);

		foreach (FishingBaitDefinition bait in _baitOptions)
		{
			_baitPicker.AddItem(bait.DisplayName);
		}

		foreach (FishingDepthDefinition depth in _depthOptions)
		{
			_depthPicker.AddItem(depth.DisplayName);
		}

		if (_baitOptions.Count > 0)
		{
			_baitPicker.Select(0);
		}

		if (_depthOptions.Count > 0)
		{
			_depthPicker.Select(0);
		}
	}

	private void ShowPreparation()
	{
		_biteController.ResetAttempt();
		_fightController.ResetFight();
		_activeRequest = new FishingEventRequest();
		_activeFish = null!;
		_setupPanel.Visible = true;
		_catchInfoLabel.Visible = false;
		_biteController.Visible = false;
		_fightController.Visible = false;
		_resultPanel.Visible = false;
		SetState(FishingState.Preparation);
		UpdateCandidatePreview();

		if (!_castButton.Disabled)
		{
			_castButton.GrabFocus();
		}
	}

	private void OnSetupChanged(long _index)
	{
		if (_state == FishingState.Preparation)
		{
			UpdateCandidatePreview();
		}
	}

	private void UpdateCandidatePreview()
	{
		if (!TryGetSelectedSetup(out FishingSetup setup))
		{
			_candidateLabel.Text = "没有可用的鱼饵或水层数据。";
			_castButton.Disabled = true;
			return;
		}

		IReadOnlyList<FishingCatchCandidate> candidates =
			_dataStore.GetCatchCandidates(setup);
		_castButton.Disabled = candidates.Count == 0;

		if (candidates.Count == 0)
		{
			_candidateLabel.Text =
				"候选池为空：当前水层没有会咬该饵的鱼。";
			return;
		}

		_candidateLabel.Text =
			$"候选池（{candidates.Count}）：\n" +
			string.Join(
				"　",
				candidates.Select(candidate =>
					$"{candidate.Fish.DisplayName}" +
					$"〔{GetDifficultyName(candidate.Fish.CatchDifficulty)}" +
					$"，权重 {candidate.Weight}〕"
				)
			);
	}

	private bool TryGetSelectedSetup(out FishingSetup setup)
	{
		setup = new FishingSetup();
		int baitIndex = _baitPicker.Selected;
		int depthIndex = _depthPicker.Selected;

		if (baitIndex < 0 || baitIndex >= _baitOptions.Count ||
			depthIndex < 0 || depthIndex >= _depthOptions.Count)
		{
			return false;
		}

		setup.BaitId = _baitOptions[baitIndex].Id;
		setup.Depth = _depthOptions[depthIndex].Value;
		return true;
	}

	private void BeginFishing()
	{
		if (!TryGetSelectedSetup(out FishingSetup setup))
		{
			return;
		}

		_activeRequest = _dataStore.CreateEventRequest(
			"test.natural_catch",
			setup
		);

		if (string.IsNullOrWhiteSpace(_activeRequest.FishId) ||
			!_dataStore.TryGetFish(
				_activeRequest.BaseFishId,
				out _activeFish))
		{
			_candidateLabel.Text = "候选池无法生成有效钓鱼请求。";
			_castButton.Disabled = true;
			return;
		}

		_setupPanel.Visible = false;
		_resultPanel.Visible = false;
		_fightController.Visible = false;
		_biteController.Visible = true;
		_catchInfoLabel.Visible = true;
		_catchInfoLabel.Text =
			$"测试可见抽取结果：{GetCatchDisplayName()} · " +
			$"{GetDifficultyName(_activeFish.CatchDifficulty)} · " +
			GetPatternName(_activeRequest.BitePattern);
		SetState(FishingState.Waiting);
		_biteController.StartAttempt(_activeRequest.BitePattern);
	}

	private void OnBiteStarted()
	{
		if (_state == FishingState.Waiting)
		{
			SetState(FishingState.Bite);
		}
	}

	private void OnBiteResolved(int resultValue)
	{
		BiteController.BiteResult result =
			(BiteController.BiteResult)resultValue;

		switch (result)
		{
			case BiteController.BiteResult.Early:
				ShowResult(false, "MISS_EARLY — 提竿过早。");
				break;

			case BiteController.BiteResult.Hooked:
				StartFight();
				break;

			case BiteController.BiteResult.Late:
				ShowResult(false, "MISS_LATE — 鱼已经松口。");
				break;
		}
	}

	private void StartFight()
	{
		_biteController.Visible = false;
		_fightController.Visible = true;
		_resultPanel.Visible = false;
		SetState(FishingState.Fight);
		_fightController.StartFight();
	}

	private void OnFightResolved(bool caught)
	{
		ShowResult(
			caught,
			caught
				? "SUCCESS — 钓获已入手。"
				: "FAIL — 鱼挣脱了钓线。"
		);
	}

	private void ShowResult(bool caught, string reason)
	{
		_biteController.ResetAttempt();
		_fightController.ResetFight();
		_setupPanel.Visible = false;
		_catchInfoLabel.Visible = false;
		_biteController.Visible = false;
		_fightController.Visible = false;
		_resultPanel.Visible = true;
		_resultLabel.Text = caught
			? $"钓获：{GetCatchDisplayName()}"
			: $"逃脱：{GetCatchDisplayName()}";
		_resultReason.Text =
			$"{reason}\n基础鱼种：{_activeFish.DisplayName} · " +
			$"难度：{GetDifficultyName(_activeFish.CatchDifficulty)} · " +
			$"咬钩：{GetPatternName(_activeRequest.BitePattern)}" +
			(_activeRequest.IsSpecialVariant ? "\n★ 本次为特殊个体" : "");
		SetState(FishingState.Result);
		_restartButton.GrabFocus();
	}

	private void SetState(FishingState state)
	{
		_state = state;
		_stateLabel.Text = state switch
		{
			FishingState.Preparation => "阶段：准备 / PREPARATION",
			FishingState.Waiting => "阶段：等待咬钩 / WAITING",
			FishingState.Bite => "阶段：咬钩 / BITE",
			FishingState.Fight => "阶段：搏鱼 / FIGHT",
			FishingState.Result => "阶段：结算 / RESULT",
			_ => state.ToString()
		};

		_instructionLabel.Text = state switch
		{
			FishingState.Preparation =>
				"选择鱼饵与水层；候选池严格取两项条件的交集。",
			FishingState.Waiting =>
				"观察浮漂。咬钩信号出现前按 Space 会判定为过早。",
			FishingState.Bite => GetBiteInstruction(),
			FishingState.Fight =>
				"使用 WASD 或方向键移动捕捉区域，让鱼留在区域内。",
			FishingState.Result =>
				"查看抽取与操作结果，然后返回准备阶段继续测试。",
			_ => string.Empty
		};
	}

	private string GetCatchDisplayName()
	{
		if (_activeRequest.IsSpecialVariant &&
			_dataStore.TryGetVariant(
				_activeRequest.FishId,
				out FishVariantDefinition variant))
		{
			return $"{variant.DisplayName}（特殊个体）";
		}

		return _activeFish?.DisplayName ?? _activeRequest.FishId;
	}

	private string GetBiteInstruction()
	{
		return _activeRequest.BitePattern switch
		{
			FishBitePattern.Probe =>
				"忽略零散点动，浮漂真正垂直下沉时按 Space。",
			FishBitePattern.Strike =>
				"准备快速反应，浮漂突然下沉后立即按 Space。",
			FishBitePattern.Drag =>
				"忽略横向游走，浮漂开始斜向下沉时按 Space。",
			_ => string.Empty
		};
	}

	private static string GetPatternName(FishBitePattern pattern)
	{
		return pattern switch
		{
			FishBitePattern.Probe => "试探型 / Probe",
			FishBitePattern.Strike => "猛咬型 / Strike",
			FishBitePattern.Drag => "拖饵型 / Drag",
			_ => pattern.ToString()
		};
	}

	private static string GetDifficultyName(FishCatchDifficulty difficulty)
	{
		return difficulty switch
		{
			FishCatchDifficulty.Low => "低难度",
			FishCatchDifficulty.Medium => "中难度",
			FishCatchDifficulty.High => "高难度",
			_ => difficulty.ToString()
		};
	}
}
