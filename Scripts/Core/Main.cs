using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public partial class Main : Control
{
	private const double CharactersPerSecond = 12.0;
	private const double SlowCharactersPerSecond = 9.0;
	private const double AcceleratedCharactersPerSecond = 72.0;
	private const double AcceleratedPunctuationScale = 0.25;
	private const double IllustrationCrossFadeDuration = 0.45;
	private static readonly Dictionary<string, string> IllustrationPaths =
		new(StringComparer.Ordinal)
		{
			["prologue.barrel.dark"] =
				"res://Assets/Art/Backgrounds/open-sea-night-fossil-subtle-v3.png",
			["prologue.barrel.crew"] =
				"res://Assets/Art/Backgrounds/open-sea-overcast-with-ship-v1.png",
			["prologue.deck.sunset"] =
				"res://Assets/Art/Backgrounds/open_sea_sunny_with_deck_background_raised.png",
			["prologue.cabin.night"] =
				"res://Assets/Art/Backgrounds/open-sea-night-fossil-subtle-v3.png",
			["act1.deck.morning"] =
				"res://Assets/Art/Backgrounds/open_sea_sunny_with_deck_background_raised.png"
		};

	private static readonly PackedScene ChoiceButtonScene = GD.Load<PackedScene>("res://Scenes/UI/DialogueChoiceButton.tscn");
	private static readonly PackedScene DinnerOptionScene = GD.Load<PackedScene>("res://Scenes/UI/BaseButton.tscn");
	private static readonly Regex CurrentBaitToken = new(@"\\*\{当前饵料\\*\}");
	private static readonly Regex TerminalCjkPunctuation =
		new(@"[。，、！？：；](?=[ \t]*\r?$)", RegexOptions.Multiline);

	[Export(PropertyHint.File, "*.json")]
	public string[] DialogueFiles { get; set; } = Array.Empty<string>();

	private Label _dialogueTitle;
	private RichTextLabel _dialogueText;
	private Control _storyStage;
	private TextureRect _previousIllustration;
	private TextureRect _currentIllustration;
	private Control _fishingStage;
	private Control _fishingCompanion;
	private DinnerStage _dinnerStage;
	private Tween _illustrationTween;
	private string _currentIllustrationId = string.Empty;
	private Control _epigraphText;
	private RichTextLabel _epigraphLabel;
	private DialogueBox _dialogueCard;
	private PanelContainer _choiceCard;
	private VBoxContainer _choicePanel;
	private Button _historyButton;
	private Control _chapterTransition;
	private Label _chapterTitle;
	private Label _chapterSubtitle;
	private AnimationPlayer _chapterAnimation;
	private Control _storyPanels;
	private Label _storyPanelTitle;
	private RichTextLabel _storyPanelContent;
	private ScrollContainer _dinnerMenu;
	private VBoxContainer _dinnerOptions;
	private Button _closeStoryPanelButton;
	private bool _showingDaySettlement;
	private Control _focusBeforeStoryPanel;
	private readonly List<string> _dialogueHistory = new();
	private DialogueFishingModule _fishingModule;
	private FishingDataStore _fishingDataStore;
	private bool _fishingTutorialBlocking;

	private DialogueGraph _dialogueGraph;
	private string _currentLineId = "";
	private GameState _gameState;
	private RichTextLabel _activeSubtitle;
	private string _activeSubtitleText = string.Empty;
	private string _activeSubtitleSpeaker = string.Empty;
	private int _visibleSubtitleCharacters;
	private int _totalSubtitleCharacters;
	private double _timeUntilNextCharacter;
	private bool _isTypingSubtitle;
	private bool _isSubtitleAccelerated;
	private double _activeCharactersPerSecond = CharactersPerSecond;
	private bool _isSilenceActive;
	private double _silenceTimeRemaining;
	private string _silenceNextId = string.Empty;
	private readonly Queue<string> _queuedFishSignals = new();

	public override void _Ready()
	{
		_gameState =
			GetNode<GameSession>("/root/GameSession").State;
		_fishingDataStore =
			GetNode<FishingDataStore>("/root/FishingDataStore");

		_storyStage = GetNode<Control>("StoryStage");
		_previousIllustration =
			GetNode<TextureRect>("StoryStage/Previous");
		_currentIllustration =
			GetNode<TextureRect>("StoryStage/Current");
		_fishingStage = GetNode<Control>("StoryStage/FishingStage");
		_fishingCompanion =
			_fishingStage.GetNode<Control>("Composition/Canvas/Aelyar");
		_dinnerStage = GetNode<DinnerStage>("DinnerStage");
		_dinnerStage.LineFinished += FinishDinnerLine;
		_epigraphText =
			GetNode<Control>("EpigraphText");

		_epigraphLabel =
			GetNode<RichTextLabel>("EpigraphText/Center/Text");

		_dialogueCard =
			GetNode<DialogueBox>("BottomArea/ContentStack/DialogueCard");

		_dialogueTitle =
			GetNode<Label>(
				"BottomArea/ContentStack/DialogueCard/DialogueStack/Header/DialogueTitle"
			);

		_dialogueText =
			GetNode<RichTextLabel>(
				"BottomArea/ContentStack/DialogueCard/DialogueStack/Body/DialogueText"
			);

		_choiceCard =
			GetNode<PanelContainer>("BottomArea/ContentStack/ChoiceCard");

		_choicePanel =
			GetNode<VBoxContainer>(
				"BottomArea/ContentStack/ChoiceCard/ChoiceMargin/ChoicePanel"
			);

		_historyButton = GetNode<Button>("HistoryButton");
		_chapterTransition = GetNode<Control>("ChapterTransition");
		_chapterTitle = _chapterTransition.GetNode<Label>("Center/Chapter/Title");
		_chapterSubtitle = _chapterTransition.GetNode<Label>("Center/Chapter/Subtitle");
		_chapterAnimation = _chapterTransition.GetNode<AnimationPlayer>("AnimationPlayer");
		_chapterAnimation.AnimationFinished += OnChapterAnimationFinished;
		_storyPanels = GetNode<Control>("StoryPanels");
		_storyPanelTitle = _storyPanels.GetNode<Label>("Panel/Stack/Title");
		_storyPanelContent = _storyPanels.GetNode<RichTextLabel>("Panel/Stack/Content");
		_dinnerMenu = _storyPanels.GetNode<ScrollContainer>("Panel/Stack/DinnerMenu");
		_dinnerOptions = _dinnerMenu.GetNode<VBoxContainer>("Options");
		_closeStoryPanelButton = _storyPanels.GetNode<Button>("Panel/Stack/Close");
		_historyButton.Pressed += ShowDialogueHistory;
		GetViewport().GuiFocusChanged += OnGuiFocusChanged;
		_closeStoryPanelButton.Pressed += CloseStoryPanel;

		_fishingModule =
			GetNode<DialogueFishingModule>("DialogueFishingModule");
		_fishingModule.FishingEventFinished +=
			OnFishingEventFinished;
		_fishingModule.TutorialStepFinished +=
			OnFishingTutorialStepFinished;
		_fishingModule.SetupSelected += setup => _gameState.CurrentFishingSetup = setup;

		DialogueLine[] lines = DialogueLoader.Load(DialogueFiles);

		if (!DialogueGraph.TryCreate(
			lines,
			out _dialogueGraph,
			out string[] validationErrors))
		{
			foreach (string error in validationErrors)
			{
				GD.PushError(error);
			}

			CancelTypewriter();
			_epigraphText.Visible = false;
			_dialogueCard.Visible = false;
			_choiceCard.Visible = false;
			return;
		}

		ConfigureCreatorMenu(lines);

		_currentLineId = _dialogueGraph.EntryId;

		ShowCurrentLine();
	}

	public override void _Process(double delta)
	{
		if (_chapterTransition.Visible)
		{
			return;
		}

		if (_fishingTutorialBlocking)
		{
			return;
		}

		if (!_fishingModule.IsEventActive && _queuedFishSignals.TryDequeue(out string eventId))
			PrepareFishingEvent(eventId);

		if (_isSilenceActive)
		{
			_silenceTimeRemaining -= delta;

			if (_silenceTimeRemaining <= 0.0)
			{
				string nextId = _silenceNextId;
				_isSilenceActive = false;
				_silenceTimeRemaining = 0.0;
				_silenceNextId = string.Empty;
				JumpTo(nextId);
			}

			return;
		}

		if (!_isTypingSubtitle || _activeSubtitle == null)
		{
			return;
		}

		_timeUntilNextCharacter -= delta;

		while (_timeUntilNextCharacter <= 0.0 &&
			_isTypingSubtitle)
		{
			RevealNextCharacter();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_chapterTransition.Visible ||
			_fishingTutorialBlocking ||
			_isSilenceActive)
		{
			return;
		}

		// 如果现在正在显示选项，
		// 普通鼠标点击不能直接推进剧情。
		if (_choiceCard.Visible)
		{
			return;
		}

		if (_dinnerStage.Visible)
		{
			if (@event.IsActionPressed("ui_accept") ||
				@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				_dinnerStage.Advance();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (@event is InputEventMouseButton mouseEvent &&
			mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (_isTypingSubtitle)
			{
				AccelerateSubtitle();

				GetViewport().SetInputAsHandled();
				return;
			}

			AdvanceDialogue();
			GetViewport().SetInputAsHandled();
		}
	}

	private void AdvanceDialogue()
	{
		if (_dialogueGraph == null ||
			!_dialogueGraph.TryGetLine(
				_currentLineId,
				out DialogueLine currentLine))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(currentLine.NextId))
		{
			return;
		}

		JumpTo(currentLine.NextId);
	}

	private void ShowCurrentLine()
	{
		if (_dialogueGraph == null ||
			!_dialogueGraph.TryGetLine(
				_currentLineId,
				out DialogueLine line))
		{
			CancelTypewriter();
			_epigraphText.Visible = false;
			_dialogueCard.Visible = false;
			_choiceCard.Visible = false;
			return;
		}

		ApplyPresentation(line);
		ApplyDialogueEffects(line.Effects);
		ApplyFishingDirective(line);
		ApplyFishingTutorialDirective(line);

		switch (line.Type)
		{
			case DialogueLineType.Dialogue:
			case DialogueLineType.Thought:
			case DialogueLineType.Narration:
			case DialogueLineType.Monologue:
				ShowDialogue(line);
				break;

			case DialogueLineType.Silence:
				ShowSilence(line);
				break;

			case DialogueLineType.Epigraph:
				ShowEpigraph(line);
				break;

			case DialogueLineType.Choice:
				ShowChoice(line);
				break;

			case DialogueLineType.FishingTutorial:
				CancelTypewriter();
				_epigraphText.Hide();
				_dialogueCard.Hide();
				_choiceCard.Hide();
				if (!_fishingTutorialBlocking)
				{
					JumpTo(line.NextId);
				}
				break;

			case DialogueLineType.DaySettlement:
				ShowDaySettlement();
				break;

			case DialogueLineType.DinnerDialogue:
				ShowDinnerLine(line);
				break;

			case DialogueLineType.ChapterTransition:
				ShowChapterTransition(line);
				break;
		}
	}

	private void ApplyPresentation(DialogueLine line)
	{
		if (!string.IsNullOrWhiteSpace(line.IllustrationId))
		{
			ShowIllustration(line.IllustrationId);
		}

		if (!string.IsNullOrWhiteSpace(line.SceneId))
		{
			ShowScene(line.SceneId);
		}
	}

	private void ShowIllustration(string illustrationId)
	{
		_fishingStage.Visible = false;

		if (_currentIllustrationId == illustrationId &&
			_storyStage.Visible)
		{
			return;
		}

		if (!IllustrationPaths.TryGetValue(
			illustrationId,
			out string texturePath))
		{
			GD.PushWarning($"未配置插图 ID：{illustrationId}");
			return;
		}

		Texture2D texture = ResourceLoader.Load<Texture2D>(texturePath);

		if (texture == null)
		{
			GD.PushError($"无法加载插图：{texturePath}");
			return;
		}

		_illustrationTween?.Kill();
		_previousIllustration.Texture = _currentIllustration.Texture;
		_previousIllustration.Modulate = Colors.White;
		_currentIllustration.Texture = texture;
		_currentIllustration.Modulate = new Color(1, 1, 1, 0);
		_currentIllustrationId = illustrationId;
		_storyStage.Visible = true;

		_illustrationTween = CreateTween();
		_illustrationTween.SetParallel(true);
		_illustrationTween.TweenProperty(
			_previousIllustration,
			"modulate:a",
			0.0f,
			IllustrationCrossFadeDuration
		);
		_illustrationTween.TweenProperty(
			_currentIllustration,
			"modulate:a",
			1.0f,
			IllustrationCrossFadeDuration
		);
		_illustrationTween.Chain().TweenCallback(
			Callable.From(() => _previousIllustration.Texture = null)
		);
	}

	private void ShowScene(string sceneId)
	{
		if (sceneId != "fishing.aelyar" &&
			sceneId != "fishing.kusin")
		{
			GD.PushWarning($"未配置场景 ID：{sceneId}");
			return;
		}

		ShowIllustration("act1.deck.morning");
		_fishingStage.Visible = true;
		// Only the companion with an existing approved visual reference is staged.
		_fishingCompanion.Visible = sceneId == "fishing.aelyar";
	}

	private void ApplyFishingTutorialDirective(DialogueLine line)
	{
		if (line.Tutorial == FishingTutorialStep.None)
		{
			return;
		}

		_fishingTutorialBlocking =
			_fishingModule.TriggerTutorialStep(
				line.Tutorial,
				_gameState.CurrentFishingSetup,
				_fishingDataStore
			);

		if (!_fishingTutorialBlocking)
		{
			GD.PushWarning(
				$"剧情 {line.Id} 请求教学步骤 {line.Tutorial}，" +
				"但钓鱼教学模块未能启动。"
			);
		}
	}

	private void ShowDialogue(DialogueLine line)
	{
		ShowDialogueCard();
		bool spoken = line.Type is DialogueLineType.Dialogue or DialogueLineType.Monologue;
		_dialogueTitle.Text = spoken ? line.Speaker : "";
		_dialogueTitle.Visible = !string.IsNullOrWhiteSpace(_dialogueTitle.Text);
		_dialogueCard.Voice = line.Type;

		StartTypewriter(_dialogueText, ResolveDialogueText(line.Text), line.Pace,
			spoken ? line.Speaker : "");
	}

	private void ShowSilence(DialogueLine line)
	{
		CancelTypewriter();
		_epigraphText.Visible = false;
		_dialogueCard.Visible = false;
		_choiceCard.Visible = false;

		_isSilenceActive = true;
		_silenceTimeRemaining = line.DurationSeconds;
		_silenceNextId = line.NextId ?? string.Empty;
	}

	private void ShowEpigraph(DialogueLine line)
	{
		CancelTypewriter();
		_dialogueCard.Visible = false;
		_choiceCard.Visible = false;
		_epigraphText.Visible = true;

		string text = ResolveDialogueText(line.Text);
		_epigraphLabel.Text = EscapeBbcode(text);
		_epigraphLabel.VisibleCharacters = -1;
		_epigraphLabel.SelfModulate = Colors.White;
		_epigraphLabel.OffsetTransformPosition =
			Vector2.Zero;
		RecordDialogue(text);
	}

	private void ShowChapterTransition(DialogueLine line)
	{
		CancelTypewriter();
		_dialogueCard.Hide();
		_choiceCard.Hide();
		_epigraphText.Hide();
		_historyButton.Hide();
		_fishingModule.SetFishingEnabled(false);
		_queuedFishSignals.Clear();

		if (line.Day is int day && day != _gameState.CurrentDay)
		{
			_gameState.BeginDay(day, new FishingSetup());
		}

		_chapterTitle.Text = line.Text;
		_chapterSubtitle.Text = line.ChapterSubtitle ?? "";
		_chapterSubtitle.Visible = !string.IsNullOrWhiteSpace(_chapterSubtitle.Text);
		_chapterTitle.Visible = !string.IsNullOrWhiteSpace(line.Text);
		_chapterTransition.Show();
		_chapterAnimation.Play(_chapterTitle.Visible || _chapterSubtitle.Visible ? "cover" : "scene_cover");
		_chapterAnimation.Advance(0.0);
	}

	private void OnChapterAnimationFinished(StringName animation)
	{
		if (animation == "reveal")
		{
			_chapterTransition.Hide();
			if (_dinnerStage.Visible && _dialogueGraph.TryGetLine(_currentLineId, out DialogueLine dinner) &&
				dinner.Type == DialogueLineType.DinnerDialogue)
			{
				PlayDinnerLine(dinner);
			}
			else
			{
				_historyButton.Show();
			}
			return;
		}

		if ((animation == "cover" || animation == "scene_cover") &&
			_dialogueGraph.TryGetLine(_currentLineId, out DialogueLine chapter))
		{
			_chapterAnimation.Play("reveal");
			// Reveal the next scene from black, including a settlement that pauses the tree.
			JumpTo(chapter.NextId);
		}
	}

	private void ShowDialogueCard()
	{
		_epigraphText.Visible = false;
		_choiceCard.Visible = false;
		_dialogueCard.Visible = true;
	}

	private void ShowChoice(DialogueLine line)
	{
		CancelTypewriter();
		_epigraphText.Visible = false;
		_dialogueCard.Visible = false;
		_choiceCard.Visible = true;

		ClearChoices();

		DialogueChoice[] choices =
			line.Choices ?? Array.Empty<DialogueChoice>();

		for (int index = 0; index < choices.Length; index++)
		{
			DialogueChoice choice = choices[index];

			Button button =
				CreateChoiceButton(choice, index);

			_choicePanel.AddChild(button);
		}

		// 默认让第一个选项获得键盘焦点。
		if (_choicePanel.GetChildCount() > 0 &&
			_choicePanel.GetChild(0) is Button firstButton)
		{
			firstButton.GrabFocus();
		}
	}

	private Button CreateChoiceButton(
		DialogueChoice choice,
		int index)
	{
		Button button = ChoiceButtonScene.Instantiate<Button>();
		button.Name = $"ChoiceButton{index + 1}";
		button.Text = ResolveDialogueText(choice.Text);

		button.Pressed += () => SelectChoice(choice, button.Text);

		return button;
	}

	private void ClearChoices()
	{
		foreach (Node child in _choicePanel.GetChildren())
		{
			_choicePanel.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void SelectChoice(DialogueChoice choice, string displayedText)
	{
		if (_fishingTutorialBlocking)
		{
			return;
		}

		RecordDialogue(displayedText, "伊诺尔");
		_choiceCard.Visible = false;
		ApplyDialogueEffects(choice.Effects);

		// 不再通过数字行号跳转，
		// 而是通过剧情 ID 跳转。
		JumpTo(choice.TargetId);
	}

	private void JumpTo(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
		{
			GD.PushError("尝试跳转到一个空的剧情 ID。");
			return;
		}

		if (_dialogueGraph != null && _dialogueGraph.TryGetLine(targetId, out DialogueLine targetLine))
		{
			bool missingCatch = !string.IsNullOrWhiteSpace(targetLine.RequiredCatchId) &&
				!_gameState.GetDailyCatch().ContainsKey(targetLine.RequiredCatchId);
			bool missingDinner = targetLine.RequiresDinnerFish &&
				_gameState.DinnerFishByDay?.ContainsKey(_gameState.CurrentDay) != true;
			if (missingCatch || missingDinner)
			{
				targetId = targetLine.FallbackId;
			}

			_currentLineId = targetId;
			ShowCurrentLine();
			return;
		}

		GD.PushError($"找不到剧情 ID：{targetId}");
	}

	private void ApplyFishingDirective(DialogueLine line)
	{
		switch (line.Fishing)
		{
			case FishingDirective.Enable:
				_fishingModule.SetFishingEnabled(true);
				_gameState.EnterDialogueFishing();
				break;

			case FishingDirective.Trigger:
				_queuedFishSignals.Enqueue(string.IsNullOrEmpty(line.FishingEventId) ? line.Id : line.FishingEventId);
				break;

			case FishingDirective.Disable:
				_queuedFishSignals.Clear();
				_fishingModule.SetFishingEnabled(false);
				break;
		}
	}

	private void PrepareFishingEvent(string eventId)
	{
		if (!_fishingModule.PrepareFishingEvent(eventId, _gameState.CurrentFishingSetup, _gameState.CurrentDay))
		{
			GD.PushWarning($"钓鱼事件 {eventId} 无法开始准备。");
		}
	}

	private void OnFishingEventFinished(FishingResult result)
	{
		_gameState.ApplyFishingResult(result);
	}

	private void OnFishingTutorialStepFinished(
		FishingTutorialStep step,
		FishingSetup setup)
	{
		_gameState.CurrentFishingSetup = setup ?? new FishingSetup();
		_fishingTutorialBlocking = false;
		AdvanceDialogue();
	}

	private string ResolveDialogueText(string text)
	{
		text ??= string.Empty;
		if (!text.Contains("当前饵料", StringComparison.Ordinal))
		{
			return text;
		}

		string baitName = _fishingDataStore.TryGetBait(
			_gameState.CurrentFishingSetup?.BaitId, out FishingBaitDefinition bait)
			? bait.DisplayName
			: "这个鱼饵";
		return CurrentBaitToken.Replace(text, _ => baitName);
	}

	private void RecordDialogue(string text, string speaker = "")
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			_dialogueHistory.Add(string.IsNullOrWhiteSpace(speaker) ? text : $"{speaker}\n{text}");
		}
	}

	private void ShowDialogueHistory()
	{
		string content = string.Join("\n\n", _dialogueHistory);
		ShowStoryPanel("历史对话", content.Length > 0 ? content : "暂无记录", "关闭");
		_storyPanelContent.ScrollToLine(_storyPanelContent.GetLineCount() - 1);
	}

	private void ShowDaySettlement()
	{
		CancelTypewriter();
		_gameState.EnterSettlement();
		_showingDaySettlement = true;

		IReadOnlyDictionary<string, int> catchByFish = _gameState.GetDailyCatch();
		List<string> rows = new();
		foreach ((string fishId, int quantity) in catchByFish)
		{
			rows.Add($"{_fishingDataStore.GetFishDisplayName(fishId)} × {quantity}");
		}

		ShowStoryPanel("今日渔获", rows.Count > 0 ? string.Join("\n", rows) : "今天没有钓到鱼", "继续");
	}

	private void ShowDinnerMenu(IReadOnlyList<FishDefinition> options)
	{
		_storyPanelTitle.Text = "选一道晚饭";
		_storyPanelContent.Hide();
		_closeStoryPanelButton.Hide();
		_dinnerMenu.Show();
		_dinnerMenu.ScrollVertical = 0;

		foreach (FishDefinition fish in options)
		{
			Button button = DinnerOptionScene.Instantiate<Button>();
			button.Text = $"{fish.DinnerRecipe.DisplayName}（{fish.DisplayName}）";
			button.TooltipText = fish.DinnerRecipe.Description;
			button.Pressed += () =>
			{
				if (_gameState.TryServeDinner(_fishingDataStore, fish.Id))
				{
					LeaveSettlement();
				}
			};
			_dinnerOptions.AddChild(button);
		}

		_dinnerOptions.GetChild<Button>(0).GrabFocus();
	}

	private void LeaveSettlement()
	{
		_showingDaySettlement = false;
		_dinnerMenu.Hide();
		foreach (Node child in _dinnerOptions.GetChildren())
		{
			_dinnerOptions.RemoveChild(child);
			child.QueueFree();
		}
		DismissStoryPanel();

		if (_dialogueGraph.TryGetLine(_currentLineId, out DialogueLine settlement) &&
			!string.IsNullOrWhiteSpace(settlement.NextId))
		{
			JumpTo(settlement.NextId);
			return;
		}

		_gameState.CompleteDay();
	}

	private void ShowDinnerLine(DialogueLine line)
	{
		CancelTypewriter();
		_dialogueCard.Hide();
		_choiceCard.Hide();
		_epigraphText.Hide();
		_historyButton.Hide();
		_dinnerStage.Show();
		if (!_chapterTransition.Visible)
		{
			PlayDinnerLine(line);
		}
	}

	private void PlayDinnerLine(DialogueLine line)
	{
		RecordDialogue(line.Text, line.Speaker);
		_dinnerStage.PlayLine(line.SpeakerId, line.Text);
	}

	private void FinishDinnerLine()
	{
		if (!_dialogueGraph.TryGetLine(_currentLineId, out DialogueLine line))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(line.NextId))
		{
			_gameState.CompleteDay();
			_historyButton.Show();
			return;
		}

		if (_dialogueGraph.TryGetLine(line.NextId, out DialogueLine next) &&
			next.Type != DialogueLineType.DinnerDialogue)
		{
			_dinnerStage.Hide();
			_gameState.CompleteDay();
		}
		JumpTo(line.NextId);
	}

	private void ShowStoryPanel(string title, string content, string closeText)
	{
		_storyPanelContent.Show();
		_closeStoryPanelButton.Show();
		_storyPanelTitle.Text = title;
		_storyPanelContent.Text = content;
		_closeStoryPanelButton.Text = closeText;
		_storyPanels.Show();
		_storyPanelContent.ScrollToLine(0);
		GetTree().Paused = true;
		_closeStoryPanelButton.GrabFocus();
	}

	private void OnGuiFocusChanged(Control control)
	{
		if (!_storyPanels.Visible && control != _historyButton)
		{
			_focusBeforeStoryPanel = control;
		}
	}

	public override void _ExitTree()
	{
		GetViewport().GuiFocusChanged -= OnGuiFocusChanged;
	}

	private void CloseStoryPanel()
	{
		if (_historyOverSettlement)
		{
			_historyOverSettlement = false;
			ShowDaySettlement();
			if (_returnToDinnerMenu)
			{
				_storyPanelTitle.Text = "选一道晚饭";
				_storyPanelContent.Hide();
				_closeStoryPanelButton.Hide();
				_dinnerMenu.Show();
				_dinnerOptions.GetChild<Button>(0).GrabFocus();
			}
			return;
		}
		if (_showingDaySettlement)
		{
			IReadOnlyList<FishDefinition> options = _gameState.GetDinnerOptions(_fishingDataStore);
			if (options.Count > 0)
			{
				ShowDinnerMenu(options);
			}
			else
			{
				LeaveSettlement();
			}
			return;
		}

		DismissStoryPanel();
	}

	private void DismissStoryPanel()
	{
		_storyPanels.Hide();
		GetTree().Paused = false;
		if (IsInstanceValid(_focusBeforeStoryPanel) && _focusBeforeStoryPanel.IsVisibleInTree())
		{
			_focusBeforeStoryPanel.GrabFocus();
		}
	}

	private void ApplyDialogueEffects(DialogueEffect[] effects)
	{
		foreach (DialogueEffect effect in
			effects ?? Array.Empty<DialogueEffect>())
		{
			_gameState.ApplyDialogueEffect(effect);
		}
	}

	private void StartTypewriter(
		RichTextLabel subtitle,
		string text,
		DialoguePace pace = DialoguePace.Normal,
		string speaker = "")
	{
		CancelTypewriter();

		_activeSubtitle = subtitle;
		_activeSubtitleText = text ?? string.Empty;
		_activeSubtitleSpeaker = speaker;
		_visibleSubtitleCharacters = 0;
		_timeUntilNextCharacter = 0.0;
		_isTypingSubtitle = true;
		_isSubtitleAccelerated = false;
		_activeCharactersPerSecond = pace == DialoguePace.Slow
			? SlowCharactersPerSecond
			: CharactersPerSecond;

		string escapedText = EscapeBbcode(_activeSubtitleText);
		// Full-width sentence endings leave a blank half-cell to the right of centered dialogue.
		int halfEm = subtitle.GetThemeFontSize("normal_font_size") / 2;
		subtitle.Text = TerminalCjkPunctuation.Replace(escapedText,
			match => $"[font gl=-{halfEm}]{match.Value}[/font]");

		subtitle.VisibleCharacters = 0;
		_totalSubtitleCharacters =
			subtitle.GetTotalCharacterCount();

		if (_totalSubtitleCharacters == 0)
			FinishTypewriter();
		else
			RevealNextCharacter();
	}

	private void RevealNextCharacter()
	{
		_visibleSubtitleCharacters++;
		_activeSubtitle.VisibleCharacters =
			_visibleSubtitleCharacters;

		if (_visibleSubtitleCharacters >=
			_totalSubtitleCharacters)
		{
			FinishTypewriter();
			return;
		}

		double punctuationDelay =
			GetPunctuationDelay(
				_activeSubtitleText,
				_visibleSubtitleCharacters - 1
			);

		if (_isSubtitleAccelerated)
		{
			punctuationDelay *=
				AcceleratedPunctuationScale;
		}

		_timeUntilNextCharacter +=
			(1.0 / (
				_isSubtitleAccelerated
					? AcceleratedCharactersPerSecond
					: _activeCharactersPerSecond
			)) +
			punctuationDelay;
	}

	private void AccelerateSubtitle()
	{
		_isSubtitleAccelerated = true;

		_timeUntilNextCharacter =
			Math.Min(
				_timeUntilNextCharacter,
				1.0 / AcceleratedCharactersPerSecond
			);
	}

	private void FinishTypewriter()
	{
		if (_activeSubtitle == null)
		{
			CancelTypewriter();
			return;
		}

		_activeSubtitle.VisibleCharacters = -1;
		_activeSubtitle.OffsetTransformPosition =
			Vector2.Zero;

		RecordDialogue(_activeSubtitleText, _activeSubtitleSpeaker);
		_isTypingSubtitle = false;
		_isSubtitleAccelerated = false;
		_activeSubtitle = null;
		_activeSubtitleText = string.Empty;
		_visibleSubtitleCharacters = 0;
		_totalSubtitleCharacters = 0;
		_timeUntilNextCharacter = 0.0;
		_activeCharactersPerSecond = CharactersPerSecond;
	}

	private void CancelTypewriter()
	{
		_isTypingSubtitle = false;
		_isSubtitleAccelerated = false;
		_activeSubtitle = null;
		_activeSubtitleText = string.Empty;
		_visibleSubtitleCharacters = 0;
		_totalSubtitleCharacters = 0;
		_timeUntilNextCharacter = 0.0;
		_activeCharactersPerSecond = CharactersPerSecond;

	}

	private static double GetPunctuationDelay(
		string text,
		int characterIndex)
	{
		if (characterIndex < 0 ||
			characterIndex >= text.Length)
		{
			return 0.0;
		}

		char character = text[characterIndex];

		if (IsPunctuation(character) &&
			characterIndex + 1 < text.Length &&
			IsPunctuation(text[characterIndex + 1]))
		{
			return 0.0;
		}

		return character switch
		{
			'。' or '！' or '？' or '!' or '?' => 0.18,
			'…' or '—' => 0.14,
			'，' or '、' or '；' or '：' or
				',' or ';' or ':' => 0.08,
			'\n' => 0.12,
			_ => 0.0
		};
	}

	private static bool IsPunctuation(char character)
	{
		return character is
			'。' or '！' or '？' or '!' or '?' or
			'…' or '—' or '，' or '、' or '；' or
			'：' or ',' or ';' or ':';
	}

	private static string EscapeBbcode(string text)
	{
		StringBuilder escapedText = new();

		foreach (char character in text)
		{
			switch (character)
			{
				case '[':
					escapedText.Append("[lb]");
					break;

				case ']':
					escapedText.Append("[rb]");
					break;

				default:
					escapedText.Append(character);
					break;
			}
		}

		return escapedText.ToString();
	}
}
