using Godot;
using System;
using System.Collections.Generic;

public partial class DialogueFishingModule : Control
{
	public event Action<FishingResult> FishingEventFinished;
	public event Action<bool> FishingInteractionBlockingChanged;
	public event Action<FishingTutorialStep, FishingSetup>
		TutorialStepFinished;

	private Control _hud = null!;
	private BiteController _biteController = null!;
	private Control _fightOverlay = null!;
	private FightController _fightController = null!;
	private Control _resultOverlay = null!;
	private Label _resultLabel = null!;
	private Label _resultReason = null!;
	private Button _continueButton = null!;
	private Control _tutorialOverlay = null!;
	private Label _tutorialTitle = null!;
	private Label _tutorialBody = null!;
	private Control _tutorialBaitRow = null!;
	private OptionButton _tutorialBaitPicker = null!;
	private Control _tutorialDepthRow = null!;
	private OptionButton _tutorialDepthPicker = null!;
	private Button _tutorialConfirmButton = null!;

	private bool _fishingEnabled;
	private bool _eventActive;
	private bool _resultVisible;
	private bool _interactionBlocking;
	private string _activeEventId = "";
	private string _activeBaseFishId = "";
	private string _activeFishId = "";
	private bool _activeIsSpecialVariant;
	private int _activeQuantity = 1;
	private FishMovementSettings _activeFightMovement = new();
	private float _activeFightGainRate = 0.32f;
	private float _activeFightLossRate = 0.08f;
	private FishingResult _lastResult = new();
	private FishingTutorialStep _activeTutorialStep =
		FishingTutorialStep.None;
	private FishingSetup _tutorialSetup = new();
	private IFishingDataRepository _tutorialDataRepository;
	private readonly List<FishingBaitDefinition> _tutorialBaits = new();
	private readonly List<FishingDepthDefinition> _tutorialDepths = new();
	private bool _firstCatchPausedForHookPrompt;
	private bool _firstCatchPausedForFightPrompt;

	public bool IsEventActive => _eventActive;

	public override void _Ready()
	{
		_hud = GetNode<Control>("Hud");
		_biteController = GetNode<BiteController>(
			"Hud/BiteController"
		);
		_fightOverlay = GetNode<Control>("FightOverlay");
		_fightController = GetNode<FightController>(
			"FightOverlay/FightController"
		);
		_resultOverlay = GetNode<Control>("ResultOverlay");
		_resultLabel = GetNode<Label>(
			"ResultOverlay/ResultFrame/ResultStack/ResultLabel"
		);
		_resultReason = GetNode<Label>(
			"ResultOverlay/ResultFrame/ResultStack/ResultReason"
		);
		_continueButton = GetNode<Button>(
			"ResultOverlay/ResultFrame/ResultStack/ContinueButton"
		);
		_tutorialOverlay = GetNode<Control>("TutorialOverlay");
		_tutorialTitle = GetNode<Label>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/TutorialTitle"
		);
		_tutorialBody = GetNode<Label>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/TutorialBody"
		);
		_tutorialBaitRow = GetNode<Control>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/BaitRow"
		);
		_tutorialBaitPicker = GetNode<OptionButton>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/BaitRow/BaitPicker"
		);
		_tutorialDepthRow = GetNode<Control>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/DepthRow"
		);
		_tutorialDepthPicker = GetNode<OptionButton>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/DepthRow/DepthPicker"
		);
		_tutorialConfirmButton = GetNode<Button>(
			"TutorialOverlay/TutorialFrame/TutorialMargin/TutorialStack/TutorialConfirmButton"
		);

		_biteController.BiteResolved += OnBiteResolved;
		_biteController.BiteStarted += OnBiteStarted;
		_fightController.FightResolved += OnFightResolved;
		_continueButton.Pressed += CompleteEvent;
		_tutorialConfirmButton.Pressed += ConfirmTutorialStep;

		SetFishingEnabled(false);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_tutorialOverlay.Visible &&
			(_activeTutorialStep == FishingTutorialStep.Cast ||
				_activeTutorialStep == FishingTutorialStep.FirstCatch) &&
			@event.IsActionPressed("fishing_hook", false))
		{
			ConfirmTutorialStep();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!_resultVisible ||
			!@event.IsActionPressed("fishing_hook", false))
		{
			return;
		}

		CompleteEvent();
		GetViewport().SetInputAsHandled();
	}

	public void SetFishingEnabled(bool enabled)
	{
		_fishingEnabled = enabled;

		if (!enabled)
		{
			CancelTutorialStep();
			CancelEvent();
			_hud.Visible = false;
			Visible = false;
			return;
		}

		Visible = true;
		_hud.Visible = true;
		_biteController.ShowIdle("浮漂已就位");
	}

	public bool TriggerTutorialStep(
		FishingTutorialStep step,
		FishingSetup setup,
		IFishingDataRepository dataRepository)
	{
		if (!_fishingEnabled ||
			_eventActive ||
			_activeTutorialStep != FishingTutorialStep.None ||
			step == FishingTutorialStep.None ||
			dataRepository == null)
		{
			return false;
		}

		_activeTutorialStep = step;
		_tutorialSetup = CopySetup(setup);
		_tutorialDataRepository = dataRepository;
		Visible = true;
		_tutorialOverlay.Visible = true;
		_tutorialBaitRow.Visible = false;
		_tutorialDepthRow.Visible = false;
		_tutorialConfirmButton.Text = "确认";

		switch (step)
		{
			case FishingTutorialStep.SelectBait:
				ConfigureBaitTutorial();
				break;

			case FishingTutorialStep.SelectDepth:
				ConfigureDepthTutorial();
				break;

			case FishingTutorialStep.Cast:
				_tutorialTitle.Text = "第三步：抛竿";
				_tutorialBody.Text =
					"按 Space 或点击下方按钮抛出鱼竿。抛竿后，" +
					"右上角的浮漂窗口会显示水面下的动静。";
				_tutorialConfirmButton.Text = "抛竿（Space）";
				break;

			case FishingTutorialStep.FirstCatch:
				_tutorialTitle.Text = "第四步：观察浮漂";
				_tutorialBody.Text =
					"开始后先观察浮漂。零散点动只是试探；" +
					"等浮漂真正沉入水中，再按 Space 提竿。";
				_tutorialConfirmButton.Text = "开始第一次钓鱼（Space）";
				break;
		}

		_tutorialConfirmButton.GrabFocus();
		return true;
	}

	public bool TriggerFishingEvent(string eventId)
	{
		return TriggerFishingEvent(
			GetNode<FishingDataStore>("/root/FishingDataStore").CreateEventRequest(eventId)
		);
	}

	public bool TriggerFishingEvent(FishingEventRequest request)
	{
		if (!_fishingEnabled ||
			_eventActive ||
			request == null ||
			string.IsNullOrWhiteSpace(request.EventId) ||
			request.Quantity < 1)
		{
			return false;
		}

		Visible = true;
		_eventActive = true;
		_resultVisible = false;
		SetInteractionBlocking(false);
		_activeEventId = request.EventId;
		_activeBaseFishId = request.BaseFishId ?? "";
		_activeFishId = request.FishId ?? "";
		_activeIsSpecialVariant = request.IsSpecialVariant;
		_activeQuantity = request.Quantity;
		_activeFightMovement = request.FightMovement;
		_activeFightGainRate = request.FightGainRate;
		_activeFightLossRate = request.FightLossRate;
		_lastResult = new FishingResult
		{
			EventId = _activeEventId,
			Outcome = FishingOutcome.None
		};
		_hud.Visible = true;
		_fightOverlay.Visible = false;
		_resultOverlay.Visible = false;
		_biteController.StartAttempt(request.BitePattern);
		return true;
	}

	private void ConfigureBaitTutorial()
	{
		_tutorialTitle.Text = "第一步：选择鱼饵";
		_tutorialBody.Text =
			"不同鱼种偏好的食物不同。先选择一种鱼饵，" +
			"之后可以根据目标鱼调整。";
		_tutorialBaitRow.Visible = true;
		_tutorialBaitPicker.Clear();
		_tutorialBaits.Clear();

		int selectedIndex = 0;

		foreach (FishingBaitDefinition bait in
			_tutorialDataRepository.Baits)
		{
			if (bait.Id == _tutorialSetup.BaitId)
			{
				selectedIndex = _tutorialBaits.Count;
			}

			_tutorialBaits.Add(bait);
			_tutorialBaitPicker.AddItem(bait.DisplayName);
		}

		if (_tutorialBaits.Count > 0)
		{
			_tutorialBaitPicker.Select(selectedIndex);
		}
	}

	private void ConfigureDepthTutorial()
	{
		_tutorialTitle.Text = "第二步：选择水层";
		_tutorialBody.Text =
			"不同鱼种活动在不同水层。浅层更容易观察，" +
			"中层和深层则可能出现不同的鱼。";
		_tutorialDepthRow.Visible = true;
		_tutorialDepthPicker.Clear();
		_tutorialDepths.Clear();

		int selectedIndex = 0;

		foreach (FishingDepthDefinition depth in
			_tutorialDataRepository.Depths)
		{
			if (depth.Value == _tutorialSetup.Depth)
			{
				selectedIndex = _tutorialDepths.Count;
			}

			_tutorialDepths.Add(depth);
			_tutorialDepthPicker.AddItem(depth.DisplayName);
		}

		if (_tutorialDepths.Count > 0)
		{
			_tutorialDepthPicker.Select(selectedIndex);
		}
	}

	private void ConfirmTutorialStep()
	{
		switch (_activeTutorialStep)
		{
			case FishingTutorialStep.SelectBait:
				if (_tutorialBaits.Count == 0)
				{
					return;
				}

				_tutorialSetup.BaitId =
					_tutorialBaits[_tutorialBaitPicker.Selected].Id;
				CompleteTutorialStep();
				break;

			case FishingTutorialStep.SelectDepth:
				if (_tutorialDepths.Count == 0)
				{
					return;
				}

				_tutorialSetup.Depth =
					_tutorialDepths[_tutorialDepthPicker.Selected].Value;
				CompleteTutorialStep();
				break;

			case FishingTutorialStep.Cast:
				_biteController.ShowIdle("浮漂已入水，等待鱼靠近……");
				CompleteTutorialStep();
				break;

			case FishingTutorialStep.FirstCatch:
				if (_firstCatchPausedForHookPrompt)
				{
					_firstCatchPausedForHookPrompt = false;
					_tutorialOverlay.Visible = false;
					_biteController.ResumeAttempt();
				}
				else if (_firstCatchPausedForFightPrompt)
				{
					_firstCatchPausedForFightPrompt = false;
					_tutorialOverlay.Visible = false;
					_fightController.ResumeFight();
				}
				else
				{
					StartFirstCatchTutorial();
				}
				break;
		}
	}

	private void StartFirstCatchTutorial()
	{
		_tutorialOverlay.Visible = false;

		FishingEventRequest request =
			_tutorialDataRepository.CreateEventRequest(
				"act1.tutorial.first_catch",
				_tutorialSetup
			);

		if (TriggerFishingEvent(request))
		{
			return;
		}

		_tutorialOverlay.Visible = true;
		_tutorialBody.Text =
			"钓鱼流程暂时无法启动，请再次尝试。";
	}

	private void CompleteTutorialStep()
	{
		FishingTutorialStep completedStep = _activeTutorialStep;
		FishingSetup completedSetup = CopySetup(_tutorialSetup);
		_activeTutorialStep = FishingTutorialStep.None;
		_firstCatchPausedForHookPrompt = false;
		_firstCatchPausedForFightPrompt = false;
		_tutorialDataRepository = null;
		_tutorialOverlay.Visible = false;
		TutorialStepFinished?.Invoke(completedStep, completedSetup);
	}

	private void CancelTutorialStep()
	{
		_activeTutorialStep = FishingTutorialStep.None;
		_firstCatchPausedForHookPrompt = false;
		_firstCatchPausedForFightPrompt = false;
		_tutorialDataRepository = null;

		if (IsInstanceValid(_tutorialOverlay))
		{
			_tutorialOverlay.Visible = false;
		}
	}

	private void OnBiteStarted()
	{
		if (_activeTutorialStep != FishingTutorialStep.FirstCatch)
		{
			return;
		}

		_biteController.PauseAttempt();
		_firstCatchPausedForHookPrompt = true;
		_tutorialTitle.Text = "第五步：辨认咬钩信号";
		_tutorialBody.Text =
			"接下来浮漂会先出现几次轻微点动。不要急着提竿；" +
			"只有当它真正向下沉时，才按 Space。";
		_tutorialConfirmButton.Text = "继续观察（Space）";
		_tutorialOverlay.Visible = true;
		_tutorialConfirmButton.GrabFocus();
	}

	private static FishingSetup CopySetup(FishingSetup setup)
	{
		setup ??= new FishingSetup();

		return new FishingSetup
		{
			BaitId = setup.BaitId ?? "",
			Depth = setup.Depth,
			WeatherId = setup.WeatherId ?? "",
			WindId = setup.WindId ?? "",
			TimeId = setup.TimeId ?? "",
			AreaId = setup.AreaId ?? ""
		};
	}

	private void OnBiteResolved(int resultValue)
	{
		BiteController.BiteResult result =
			(BiteController.BiteResult)resultValue;

		switch (result)
		{
			case BiteController.BiteResult.Early:
				SetInteractionBlocking(true);
				ShowResult(
					FishingResult.CreateFailure(
						_activeEventId,
						FishingOutcome.MissedEarly
					),
					"MISS_EARLY — You lifted too soon."
				);
				break;

			case BiteController.BiteResult.Hooked:
				SetInteractionBlocking(true);
				_hud.Visible = false;
				_fightOverlay.Visible = true;
				_fightController.StartFight(_activeFightMovement,
					gainRate: _activeFightGainRate, lossRate: _activeFightLossRate);

				if (_activeTutorialStep ==
					FishingTutorialStep.FirstCatch)
				{
					_fightController.PauseFight();
					_firstCatchPausedForFightPrompt = true;
					_tutorialTitle.Text = "第六步：搏鱼";
					_tutorialBody.Text =
						"使用方向键或 WASD 移动收线区，" +
						"让浅色鱼影保持在其中，直到进度填满。鱼潜入水下时仍会继续游动。";
					_tutorialConfirmButton.Text = "开始搏鱼（Space）";
					_tutorialOverlay.Visible = true;
					_tutorialConfirmButton.GrabFocus();
				}
				break;

			case BiteController.BiteResult.Late:
				if (!_biteController.LastResolutionWasPlayerInitiated)
				{
					CompleteMissWithoutBlocking(
						FishingResult.CreateFailure(
							_activeEventId,
							FishingOutcome.MissedLate
						)
					);
					break;
				}

				SetInteractionBlocking(true);
				ShowResult(
					FishingResult.CreateFailure(
						_activeEventId,
						FishingOutcome.MissedLate
					),
					"MISS_LATE — The fish let go."
				);
				break;
		}
	}

	private void OnFightResolved(bool caught)
	{
		FishingResult result = caught
			? FishingResult.CreateCatch(
				_activeEventId,
				_activeFishId,
				_activeQuantity,
				_activeBaseFishId,
				_activeIsSpecialVariant
			)
			: FishingResult.CreateFailure(
				_activeEventId,
				FishingOutcome.Escaped
			);

		ShowResult(
			result,
			caught
				? "SUCCESS — Return to the conversation."
				: "FAIL — The line went slack."
		);
	}

	private void ShowResult(FishingResult result, string reason)
	{
		SetInteractionBlocking(true);
		_biteController.ResetAttempt();
		_fightController.ResetFight();
		_hud.Visible = false;
		_fightOverlay.Visible = false;
		_resultOverlay.Visible = true;
		_resultVisible = true;
		_lastResult = result;
		_resultLabel.Text = result.Caught ? "成功收竿" : "鱼挣脱了";
		_resultReason.Text = reason;
		_continueButton.GrabFocus();
	}

	private void CompleteEvent()
	{
		if (!_eventActive || !_resultVisible)
		{
			return;
		}

		FishingResult result = _lastResult;
		bool completesFirstCatchTutorial =
			_activeTutorialStep == FishingTutorialStep.FirstCatch;
		_eventActive = false;
		_resultVisible = false;
		_activeEventId = "";
		_activeBaseFishId = "";
		_activeFishId = "";
		_activeIsSpecialVariant = false;
		_activeQuantity = 1;
		_resultOverlay.Visible = false;
		_fightOverlay.Visible = false;
		_hud.Visible = _fishingEnabled;

		if (_fishingEnabled)
		{
			_biteController.ShowIdle("浮漂已就位");
		}

		FishingEventFinished?.Invoke(result);
		SetInteractionBlocking(false);

		if (completesFirstCatchTutorial)
		{
			CompleteTutorialStep();
		}
	}

	private void CompleteMissWithoutBlocking(FishingResult result)
	{
		if (!_eventActive)
		{
			return;
		}

		_eventActive = false;
		_resultVisible = false;
		_activeEventId = "";
		_activeBaseFishId = "";
		_activeFishId = "";
		_activeIsSpecialVariant = false;
		_activeQuantity = 1;
		_lastResult = result;
		_fightOverlay.Visible = false;
		_resultOverlay.Visible = false;
		_hud.Visible = _fishingEnabled;

		if (_fishingEnabled)
		{
			_biteController.ShowIdle("鱼讯错过了，继续等待……");
		}

		SetInteractionBlocking(false);
		FishingEventFinished?.Invoke(result);
	}

	private void SetInteractionBlocking(bool blocking)
	{
		if (_interactionBlocking == blocking)
		{
			return;
		}

		_interactionBlocking = blocking;
		FishingInteractionBlockingChanged?.Invoke(blocking);
	}

	private void CancelEvent()
	{
		SetInteractionBlocking(false);
		_eventActive = false;
		_resultVisible = false;
		_activeEventId = "";
		_activeBaseFishId = "";
		_activeFishId = "";
		_activeIsSpecialVariant = false;
		_activeQuantity = 1;
		_lastResult = new FishingResult
		{
			Outcome = FishingOutcome.Cancelled
		};
		_biteController?.ResetAttempt();
		_fightController?.ResetFight();

		if (IsInstanceValid(_fightOverlay))
		{
			_fightOverlay.Visible = false;
		}

		if (IsInstanceValid(_resultOverlay))
		{
			_resultOverlay.Visible = false;
		}
	}
}
