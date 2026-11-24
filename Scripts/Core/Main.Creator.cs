using Godot;
using System;
using System.Linq;

public partial class Main
{
	private DialogueLine[] _creatorLines;
	private bool _historyOverSettlement;
	private bool _returnToDinnerMenu;

	private void ConfigureCreatorMenu(DialogueLine[] lines)
	{
		_creatorLines = lines;
		CreatorMenu menu = GetNode<CreatorMenu>("CreatorMenu");
		menu.CurrentLocation = () => _currentLineId;
		menu.ModalChanged += open =>
		{
			var mode = open ? ProcessModeEnum.Disabled : ProcessModeEnum.Always;
			_storyPanels.ProcessMode = mode;
			_chapterTransition.ProcessMode = mode;
		};
		string section = "序章";
		foreach (DialogueLine line in lines)
		{
			if (line.Type == DialogueLineType.ChapterTransition)
				section = string.IsNullOrWhiteSpace(line.Text + line.ChapterSubtitle)
					? "晚餐" : (line.Text + " " + line.ChapterSubtitle).Trim();
			string caption = $"{line.Speaker} {line.Text}".Trim();
			if (line.Type == DialogueLineType.ChapterTransition)
				caption = section;
			else if (line.Type == DialogueLineType.Choice)
				caption = string.Join(" / ", line.Choices.Select(choice => choice.Text));
			menu.Register(section, $"{line.Id}　{caption}", () => CreatorJump(line));
		}

		void Feature(string title, Action action) => menu.Register("功能", title, action);
		foreach (DialogueLine tutorial in lines.Where(line => line.Tutorial != FishingTutorialStep.None))
		{
			string label = tutorial.Tutorial switch
			{
				FishingTutorialStep.SelectBait => "选择鱼饵",
				FishingTutorialStep.SelectDepth => "选择水层",
				FishingTutorialStep.Cast => "抛竿教学",
				FishingTutorialStep.FirstCatch => "首次钓鱼 · 提竿与搏鱼教学",
				FishingTutorialStep.SelectShiftBait => "换班选饵",
				_ => tutorial.Tutorial.ToString()
			};
			Feature(label, () => CreatorJump(tutorial));
		}
		var triggers = lines.Where(line => line.Fishing == FishingDirective.Trigger);
		foreach (var trigger in triggers)
			Feature($"触发钓鱼　{trigger.FishingEventId}　{trigger.Id}", () => CreatorJump(trigger));
		var anchor = triggers.FirstOrDefault();
		if (anchor != null)
		{
			foreach (var fishingEvent in _fishingDataStore.ScriptedEvents)
			{
				void Preview(bool fight, FishingOutcome outcome)
				{
					CreatorJump(anchor);
					_queuedFishSignals.Clear();
					_fishingModule.PreviewEvent(fishingEvent.Id, fight, outcome);
				}
				string fish = _fishingDataStore.GetFishDisplayName(fishingEvent.FishId);
				Feature($"浮漂与提竿　{fish}", () => Preview(false, FishingOutcome.None));
				Feature($"直接搏鱼　{fish}", () => Preview(true, FishingOutcome.None));
				foreach (var (outcome, label) in new[] {
					(FishingOutcome.Caught, "成功上鱼"), (FishingOutcome.NoBite, "没有鱼讯"),
					(FishingOutcome.MissedEarly, "提竿太早"), (FishingOutcome.MissedLate, "错过鱼讯"),
					(FishingOutcome.Escaped, "鱼挣脱了") })
					Feature($"钓鱼结果　{label} · {fish}", () => Preview(false, outcome));
			}
		}
		foreach (var settlement in lines.Where(line => line.Type == DialogueLineType.DaySettlement))
		{
			Feature("日末渔获结算", () => CreatorJump(settlement));
			Feature("晚餐选菜（补齐当日食材并重新选菜）", () =>
			{
				CreatorJump(settlement);
				foreach (var fish in _fishingDataStore.Fish.Where(f => f.DinnerRecipe != null))
					if (!_gameState.GetDailyCatch().ContainsKey(fish.Id))
						_gameState.ApplyFishingResult(FishingResult.CreateCatch("creator.dinner", fish.Id));
				_gameState.DinnerFishByDay.Remove(_gameState.CurrentDay);
				CloseStoryPanel();
			});
		}
		Feature("历史对话", () =>
		{
			if (_showingDaySettlement && !_historyOverSettlement)
			{
				_historyOverSettlement = true;
				_returnToDinnerMenu = _dinnerMenu.Visible;
				_dinnerMenu.Hide();
			}
			ShowDialogueHistory();
		});
	}

	private void CreatorJump(DialogueLine target)
	{
		CancelTypewriter();
		_chapterAnimation.Stop();
		_chapterTransition.Hide();
		_dinnerStage.StopLine();
		_dinnerStage.Hide();
		_showingDaySettlement = false;
		_historyOverSettlement = false;
		_dinnerMenu.Hide();
		foreach (Node child in _dinnerOptions.GetChildren())
		{
			_dinnerOptions.RemoveChild(child);
			child.QueueFree();
		}
		DismissStoryPanel();
		_dialogueCard.Hide();
		_choiceCard.Hide();
		_epigraphText.Hide();
		_historyButton.Show();
		_isSilenceActive = _fishingTutorialBlocking = false;
		_fishingModule.SetFishingEnabled(false);
		_queuedFishSignals.Clear();
		_illustrationTween?.Kill();
		_currentIllustrationId = "";
		_previousIllustration.Texture = _currentIllustration.Texture = null;
		_fishingStage.Hide();

		int day = 0;
		string illustration = "", scene = "";
		bool fishing = false;
		foreach (var line in _creatorLines)
		{
			if (line.Id == target.Id) break;
			day = line.Day ?? day;
			if (IllustrationPaths.ContainsKey(line.IllustrationId))
			{
				illustration = line.IllustrationId;
				scene = "";
			}
			if (line.SceneId.Length > 0) scene = line.SceneId;
			if (line.Type == DialogueLineType.ChapterTransition || line.Fishing == FishingDirective.Disable)
				fishing = false;
			if (line.Fishing == FishingDirective.Enable) fishing = true;
		}
		_gameState.CurrentDay = day;
		_gameState.CurrentPhase = day == 0 ? DayPhase.Prologue : DayPhase.Preparation;
		if (target.Type == DialogueLineType.DinnerDialogue) _gameState.EnterSettlement();
		if (scene.Length > 0) ShowScene(scene);
		else if (illustration.Length > 0) ShowIllustration(illustration);
		if (fishing)
		{
			if (!_fishingDataStore.TryGetBait(_gameState.CurrentFishingSetup.BaitId, out _) &&
				_fishingDataStore.Baits.FirstOrDefault() is { } bait)
				_gameState.CurrentFishingSetup.BaitId = bait.Id;
			_gameState.EnterDialogueFishing();
			_fishingModule.SetFishingEnabled(true);
		}
		if (fishing) _fishingModule.ShowWaitingBobber();
		_currentLineId = target.Id;
		ShowCurrentLine();
	}
}
