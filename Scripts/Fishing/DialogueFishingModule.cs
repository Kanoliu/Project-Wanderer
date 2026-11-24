using Godot;
using System;
using System.Collections.Generic;

public partial class DialogueFishingModule : Control
{
	private const string FirstCatchEventId = "act1.tutorial.first_catch";

	public event Action<FishingResult> FishingEventFinished;
	public event Action<FishingTutorialStep, FishingSetup>
		TutorialStepFinished;
	public event Action<FishingSetup> SetupSelected;

	[Export] public BiteController BobberHud { get; set; } = null!;

	private FishingDataStore _fishingData = null!;
	private Control _fightOverlay = null!;
	private FightController _fightController = null!;
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
		_fishingData = GetNode<FishingDataStore>("/root/FishingDataStore");
		_fightOverlay = GetNode<Control>("FightOverlay");
		_fightController = GetNode<FightController>(
			"FightOverlay/FightController"
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

		BobberHud.BiteResolved += OnBiteResolved;
		BobberHud.HookWindowHeld += OnHookWindowHeld;
		_fightController.FightResolved += OnFightResolved;
		_tutorialConfirmButton.Pressed += ConfirmTutorialStep;

		SetFishingEnabled(false);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_resultVisible && _tutorialOverlay.Visible &&
			(_activeTutorialStep == FishingTutorialStep.Cast ||
				_activeTutorialStep == FishingTutorialStep.FirstCatch) &&
			@event.IsActionPressed("fishing_hook", false))
		{
			ConfirmTutorialStep();
			GetViewport().SetInputAsHandled();
			return;
		}

	}

	public void SetFishingEnabled(bool enabled)
	{
		_fishingEnabled = enabled;

		if (!enabled)
		{
			CancelTutorialStep();
			CancelEvent();
			BobberHud.Hide();
			Visible = false;
			return;
		}

		Visible = true;
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

		if ((step is FishingTutorialStep.SelectBait or FishingTutorialStep.SelectDepth or
			FishingTutorialStep.FirstCatch) &&
			(!dataRepository.TryGetScriptedEvent(FirstCatchEventId, out var tutorialEvent) ||
				!dataRepository.TryGetFish(tutorialEvent.FishId, out _tutorialFish)))
		{
			return false;
		}

		_activeTutorialStep = step;
		_tutorialSetup = CopySetup(setup);
		_tutorialDataRepository = dataRepository;
		if (step is FishingTutorialStep.SelectBait or FishingTutorialStep.SelectShiftBait) BobberHud.Hide();
		_tutorialBaitRow.Visible = false;
		_tutorialDepthRow.Visible = false;

		switch (step)
		{
			case FishingTutorialStep.SelectBait:
			case FishingTutorialStep.SelectShiftBait:
				ConfigureBaitPicker();
				return true;

			case FishingTutorialStep.SelectDepth:
				ConfigureDepthPicker(_tutorialSetup, _tutorialFish);
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

	public void PreviewEvent(string eventId, bool fight, FishingOutcome outcome)
	{
		if (!TriggerFishingEvent(eventId)) return;
		if (fight)
		{
			BobberHud.ResetAttempt();
			OnBiteResolved((int)BiteController.BiteResult.Hooked);
		}
		else if (outcome == FishingOutcome.Caught) OnFightResolved(true);
		else if (outcome != FishingOutcome.None)
			ShowResult(FishingResult.CreateFailure(eventId, outcome));
	}

	public bool TriggerFishingEvent(string eventId)
	{
		return TriggerFishingEvent(
			_fishingData.CreateEventRequest(eventId)
		);
	}

	public bool TriggerFishingEvent(FishingEventRequest request)
	{
		return StartFishingEvent(request, false);
	}

	public bool PrepareFishingEvent(string eventId, FishingSetup setup, int day)
	{
		if (!_fishingEnabled || _eventActive || _activeTutorialStep != FishingTutorialStep.None)
			return false;

		_tutorialSetup = CopySetup(setup);
		var request = _fishingData.CreateEventRequest(eventId, setup, day);
		if (!StartFishingEvent(request, false)) return false;
		if (string.IsNullOrEmpty(request.FishId))
			ShowResult(FishingResult.CreateFailure(eventId, FishingOutcome.NoBite));
		return true;
	}

	public void ShowWaitingBobber()
	{
		BobberHud.ShowIdle("");
		BobberHud.Show();
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
		BobberHud.Show();
		_fightOverlay.Visible = false;
		BobberHud.StartAttempt(request.BitePattern, firstCatch);
		return true;
	}

	private void ConfigureBaitPicker()
	{
		_tutorialBaitRow.Visible = true;
		_tutorialBaitPicker.Clear();
		_tutorialBaits.Clear();

		int selectedIndex = 0;

		foreach (FishingBaitDefinition bait in
			_tutorialDataRepository.Baits)
		{
			if (_tutorialFish != null &&
				Array.IndexOf(_tutorialFish.Availability.BaitIds, bait.Id) < 0)
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

	private void ConfigureDepthPicker(FishingSetup setup, FishDefinition tutorialFish, string action = "设定水层")
	{
		_tutorialDepthRow.Visible = true;
		_tutorialDepthPicker.Clear();
		_tutorialDepths.Clear();

		int selectedIndex = 0;

		foreach (FishingDepthDefinition depth in _fishingData.Depths)
		{
			if (tutorialFish != null &&
				Array.IndexOf(tutorialFish.Availability.DepthIds, depth.Id) < 0)
			{
				continue;
			}

			if (depth.Value == setup.Depth)
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

		ShowTutorialPrompt(action);
		_tutorialDepthPicker.GrabFocus();
	}

	private void ConfirmTutorialStep()
	{
		if (_resultVisible)
		{
			CompleteEvent();
			return;
		}

		switch (_activeTutorialStep)
		{
			case FishingTutorialStep.SelectBait:
			case FishingTutorialStep.SelectShiftBait:
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
					BobberHud.ResumeAttempt();
					BobberHud.Hook();
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
				_tutorialSetup,
				day: 1
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
		ShowWaitingBobber();
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
				ShowResult(
					FishingResult.CreateFailure(
						_activeEventId,
						FishingOutcome.MissedEarly
					)
				);
				break;

			case BiteController.BiteResult.Hooked:
				BobberHud.Hide();
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
		BobberHud.ResetAttempt();
		_fightController.ResetFight();
		BobberHud.Hide();
		_fightOverlay.Visible = false;
		_resultVisible = true;
		_lastResult = result;
		bool firstCatch = _activeTutorialStep == FishingTutorialStep.FirstCatch;
		string summary = result.Outcome switch
		{
			FishingOutcome.Caught when !string.IsNullOrEmpty(result.FishId) =>
				$"钓到{_fishingData.GetFishDisplayName(result.FishId)} × {result.Quantity}",
			FishingOutcome.Caught => "成功收竿",
			FishingOutcome.MissedEarly when firstCatch => "等浮漂沉下去再提竿。",
			FishingOutcome.MissedEarly => "提竿太早",
			FishingOutcome.MissedLate => "错过鱼讯",
			FishingOutcome.NoBite => "这一竿没有鱼讯",
			_ => "鱼挣脱了"
		};
		_tutorialDataRepository = _fishingData;
		if (!firstCatch || result.Caught) _tutorialFish = null;
		ConfigureBaitPicker();
		ConfigureDepthPicker(_tutorialSetup, _tutorialFish);
		ShowTutorialPrompt("抛下一竿", summary);
		_tutorialBaitPicker.GrabFocus();
		FishingEventFinished?.Invoke(result);
	}

	private void CompleteEvent()
	{
		if (!_eventActive || !_resultVisible || _tutorialBaitPicker.Selected < 0 ||
			_tutorialDepthPicker.Selected < 0) return;

		_tutorialSetup.BaitId = _tutorialBaits[_tutorialBaitPicker.Selected].Id;
		_tutorialSetup.Depth = _tutorialDepths[_tutorialDepthPicker.Selected].Value;
		SetupSelected?.Invoke(CopySetup(_tutorialSetup));
		_eventActive = false;
		_resultVisible = false;
		_tutorialOverlay.Hide();

		if (_activeTutorialStep == FishingTutorialStep.FirstCatch)
		{
			if (_lastResult.Caught) CompleteTutorialStep();
			else StartFirstCatchTutorial();
			return;
		}
		ShowWaitingBobber();
	}

	private void CancelEvent()
	{
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
		BobberHud?.ResetAttempt();
		_fightController?.ResetFight();

		if (IsInstanceValid(_fightOverlay))
		{
			_fightOverlay.Visible = false;
		}

	}
}
