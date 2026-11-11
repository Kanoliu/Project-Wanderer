using Godot;
using System;
using System.Collections.Generic;

public partial class DialogueFishingModule : Control
{
	private const string FirstCatchEventId = "act1.tutorial.first_catch";

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
	private Button _continueButton = null!;
	private Control _tutorialOverlay = null!;
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
	private FishDefinition _tutorialFish;
	private readonly List<FishingBaitDefinition> _tutorialBaits = new();
	private readonly List<FishingDepthDefinition> _tutorialDepths = new();
	private bool _firstCatchPausedForHookPrompt;
	private bool _firstCatchPausedForFightPrompt;

	public bool IsEventActive => _eventActive;
	public bool IsFishingEnabled => _fishingEnabled;

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
		_continueButton = GetNode<Button>(
			"ResultOverlay/ResultFrame/ResultStack/ContinueButton"
		);
		_tutorialOverlay = GetNode<Control>("TutorialOverlay");
		_tutorialBody = GetNode<Label>(
			"TutorialOverlay/TutorialFrame/TutorialStack/TutorialBody"
		);
		_tutorialBaitRow = GetNode<Control>(
			"TutorialOverlay/TutorialFrame/TutorialStack/BaitRow"
		);
		_tutorialBaitPicker = GetNode<OptionButton>(
			"TutorialOverlay/TutorialFrame/TutorialStack/BaitRow/BaitPicker"
		);
		_tutorialDepthRow = GetNode<Control>(
			"TutorialOverlay/TutorialFrame/TutorialStack/DepthRow"
		);
		_tutorialDepthPicker = GetNode<OptionButton>(
			"TutorialOverlay/TutorialFrame/TutorialStack/DepthRow/DepthPicker"
		);
		_tutorialConfirmButton = GetNode<Button>(
			"TutorialOverlay/TutorialFrame/TutorialStack/TutorialConfirmButton"
		);

		_biteController.BiteResolved += OnBiteResolved;
		_biteController.HookWindowHeld += OnHookWindowHeld;
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
			dataRepository == null ||
			!dataRepository.TryGetScriptedEvent(FirstCatchEventId, out var tutorialEvent) ||
			!dataRepository.TryGetFish(tutorialEvent.FishId, out _tutorialFish))
		{
			return false;
		}

		_activeTutorialStep = step;
		_tutorialSetup = CopySetup(setup);
		_tutorialDataRepository = dataRepository;
		_hud.Hide();
		_tutorialBaitRow.Visible = false;
		_tutorialDepthRow.Visible = false;

		switch (step)
		{
			case FishingTutorialStep.SelectBait:
				ConfigureBaitTutorial();
				return true;

			case FishingTutorialStep.SelectDepth:
				ConfigureDepthTutorial();
				return true;

			case FishingTutorialStep.Cast:
				ShowTutorialPrompt("抛竿 · 空格");
				return true;

			case FishingTutorialStep.FirstCatch:
				if (StartFirstCatchTutorial())
				{
					return true;
				}
				break;
		}

		CancelTutorialStep();
		return false;
	}

	public bool TriggerFishingEvent(string eventId)
	{
		return TriggerFishingEvent(
			GetNode<FishingDataStore>("/root/FishingDataStore").CreateEventRequest(eventId)
		);
	}

	public bool TriggerFishingEvent(FishingEventRequest request)
	{
		return StartFishingEvent(request, false);
	}

	private bool StartFishingEvent(FishingEventRequest request, bool firstCatch)
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
		_biteController.StartAttempt(request.BitePattern, firstCatch);
		return true;
	}

	private void ConfigureBaitTutorial()
	{
		_tutorialBaitRow.Visible = true;
		_tutorialBaitPicker.Clear();
		_tutorialBaits.Clear();

		int selectedIndex = 0;

		foreach (FishingBaitDefinition bait in
			_tutorialDataRepository.Baits)
		{
			if (Array.IndexOf(_tutorialFish.Availability.BaitIds, bait.Id) < 0)
			{
				continue;
			}

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

		ShowTutorialPrompt("挂上鱼饵");
		_tutorialBaitPicker.GrabFocus();
	}

	private void ConfigureDepthTutorial()
	{
		_tutorialDepthRow.Visible = true;
		_tutorialDepthPicker.Clear();
		_tutorialDepths.Clear();

		int selectedIndex = 0;

		foreach (FishingDepthDefinition depth in
			_tutorialDataRepository.Depths)
		{
			if (Array.IndexOf(_tutorialFish.Availability.DepthIds, depth.Id) < 0)
			{
				continue;
			}

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

		ShowTutorialPrompt("设定水层");
		_tutorialDepthPicker.GrabFocus();
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
				CompleteTutorialStep();
				break;

			case FishingTutorialStep.FirstCatch:
				if (_firstCatchPausedForHookPrompt)
				{
					_firstCatchPausedForHookPrompt = false;
					_tutorialOverlay.Visible = false;
					_biteController.ResumeAttempt();
					_biteController.Hook();
				}
				else if (_firstCatchPausedForFightPrompt)
				{
					_firstCatchPausedForFightPrompt = false;
					_tutorialOverlay.Visible = false;
					_fightController.ResumeFight();
				}
				break;
		}
	}

	private bool StartFirstCatchTutorial()
	{
		FishingEventRequest request =
			_tutorialDataRepository.CreateEventRequest(
				FirstCatchEventId,
				_tutorialSetup
			);
		// The first fight teaches tracking before introducing bursts and dives.
		request.FightMovement.CruiseSpeed *= 0.55f;
		request.FightMovement.BurstDuration = 0.0f;
		request.FightMovement.DiveRate = 0.0f;
		request.FightGainRate = 0.20f;
		return StartFishingEvent(request, true);
	}

	private void CompleteTutorialStep()
	{
		FishingTutorialStep completedStep = _activeTutorialStep;
		FishingSetup completedSetup = CopySetup(_tutorialSetup);
		_activeTutorialStep = FishingTutorialStep.None;
		_firstCatchPausedForHookPrompt = false;
		_firstCatchPausedForFightPrompt = false;
		_tutorialDataRepository = null;
		_tutorialFish = null;
		_tutorialOverlay.Visible = false;
		TutorialStepFinished?.Invoke(completedStep, completedSetup);
	}

	private void CancelTutorialStep()
	{
		_activeTutorialStep = FishingTutorialStep.None;
		_firstCatchPausedForHookPrompt = false;
		_firstCatchPausedForFightPrompt = false;
		_tutorialDataRepository = null;
		_tutorialFish = null;

		if (IsInstanceValid(_tutorialOverlay))
		{
			_tutorialOverlay.Visible = false;
		}
	}

	private void ShowTutorialPrompt(string action, string hint = "")
	{
		_tutorialBody.Text = hint;
		_tutorialBody.Visible = hint.Length > 0;
		_tutorialConfirmButton.Text = action;
		_tutorialOverlay.Show();
		_tutorialConfirmButton.GrabFocus();
	}

	private void OnHookWindowHeld()
	{
		_firstCatchPausedForHookPrompt = true;
		ShowTutorialPrompt("提竿 · 空格");
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
					)
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
					ShowTutorialPrompt("开始收线", "用方向键或 WASD，让鱼影留在框内。");
				}
				break;

			case BiteController.BiteResult.Late:
				if (_activeTutorialStep != FishingTutorialStep.FirstCatch &&
					!_biteController.LastResolutionWasPlayerInitiated)
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
					)
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

		ShowResult(result);
	}

	private void ShowResult(FishingResult result)
	{
		SetInteractionBlocking(true);
		_biteController.ResetAttempt();
		_fightController.ResetFight();
		_hud.Visible = false;
		_fightOverlay.Visible = false;
		_resultOverlay.Visible = true;
		_resultVisible = true;
		_lastResult = result;
		bool firstCatch = _activeTutorialStep == FishingTutorialStep.FirstCatch;
		_resultLabel.Text = firstCatch
			? result.Outcome switch
			{
				FishingOutcome.Caught => $"钓到{_tutorialFish.DisplayName}",
				FishingOutcome.MissedEarly => "等浮漂沉下去再提竿。",
				_ => "鱼挣脱了"
			}
			: result.Caught ? "成功收竿" : "鱼挣脱了";
		_continueButton.Text = firstCatch && !result.Caught ? "再抛一竿" : "继续";
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

		if (completesFirstCatchTutorial)
		{
			if (!result.Caught && StartFirstCatchTutorial())
			{
				return;
			}

			CompleteTutorialStep();
		}

		SetInteractionBlocking(false);
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
