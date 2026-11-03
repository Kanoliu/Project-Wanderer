using Godot;
using System;
using System.Collections.Generic;
using System.Text;

public partial class Main : Control
{
	private const double CharactersPerSecond = 12.0;
	private const double SlowCharactersPerSecond = 9.0;
	private const double AcceleratedCharactersPerSecond = 72.0;
	private const double AcceleratedPunctuationScale = 0.25;
	private const double SubtitleEntranceDuration = 0.18;
	private const double IllustrationCrossFadeDuration = 0.45;
	private const double SameSpeakerTransitionDelay = 0.5;
	private const double SpeakerChangeTransitionDelay = 1.0;
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

	private static readonly PackedScene ChoiceButtonScene = GD.Load<PackedScene>("res://Scenes/UI/BaseButton.tscn");
	private Label _dialogueTitle;
	private RichTextLabel _dialogueText;
	private Control _storyStage;
	private TextureRect _previousIllustration;
	private TextureRect _currentIllustration;
	private Tween _illustrationTween;
	private string _currentIllustrationId = string.Empty;
	private Control _epigraphText;
	private RichTextLabel _epigraphLabel;
	private PanelContainer _dialogueCard;
	private PanelContainer _choiceCard;
	private VBoxContainer _choicePanel;
	private DialogueFishingModule _fishingModule;
	private FishingDataStore _fishingDataStore;
	private bool _fishingEventBlocking;
	private bool _fishingTutorialBlocking;

	private DialogueGraph _dialogueGraph;
	private string _currentLineId = "";
	private GameState _gameState;
	private RichTextLabel _activeSubtitle;
	private string _activeSubtitleText = string.Empty;
	private int _visibleSubtitleCharacters;
	private int _totalSubtitleCharacters;
	private double _timeUntilNextCharacter;
	private bool _isTypingSubtitle;
	private bool _isSubtitleAccelerated;
	private double _activeCharactersPerSecond = CharactersPerSecond;
	private Tween _subtitleEntranceTween;
	private bool _isSilenceActive;
	private double _silenceTimeRemaining;
	private string _silenceNextId = string.Empty;
	private bool _isLineTransitionActive;
	private double _lineTransitionTimeRemaining;
	private string _lineTransitionTargetId = string.Empty;
	private string _lastSpokenSpeaker = string.Empty;
	private readonly Dictionary<string, DialogueFishingTriggerWindow>
		_fishingTriggerWindowsByStartId =
			new(StringComparer.Ordinal);
	private readonly HashSet<string> _completedFishingTriggerWindowIds =
		new(StringComparer.Ordinal);
	private DialogueFishingTriggerWindow _activeFishingTriggerWindow;
	private double _fishingTriggerTimeRemaining;

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
		_epigraphText =
			GetNode<Control>("EpigraphText");

		_epigraphLabel =
			GetNode<RichTextLabel>("EpigraphText/Center/Text");

		_dialogueCard =
			GetNode<PanelContainer>("BottomArea/ContentStack/DialogueCard");

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

		_fishingModule =
			GetNode<DialogueFishingModule>("DialogueFishingModule");
		_fishingModule.FishingEventFinished +=
			OnFishingEventFinished;
		_fishingModule.FishingInteractionBlockingChanged +=
			OnFishingInteractionBlockingChanged;
		_fishingModule.TutorialStepFinished +=
			OnFishingTutorialStepFinished;

		DialogueLine[] lines = DialogueLoader.Load(
			"res://Dialogue/prologue.json",
			"res://Dialogue/act_one.json"
		);

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

		ConfigureFishingTriggerWindows();

		_currentLineId = _dialogueGraph.EntryId;

		ShowCurrentLine();
	}

	public override void _Process(double delta)
	{
		if (_fishingEventBlocking)
		{
			return;
		}

		if (_fishingTutorialBlocking)
		{
			return;
		}

		UpdateFishingTriggerWindow(delta);

		if (_fishingEventBlocking)
		{
			return;
		}

		if (_isSilenceActive)
		{
			_silenceTimeRemaining -= delta;

			if (_silenceTimeRemaining <= 0.0)
			{
				if (_activeFishingTriggerWindow != null &&
					_activeFishingTriggerWindow.EndId == _currentLineId)
				{
					TriggerActiveFishingWindow();

					if (_fishingEventBlocking)
					{
						return;
					}
				}

				string nextId = _silenceNextId;
				_isSilenceActive = false;
				_silenceTimeRemaining = 0.0;
				_silenceNextId = string.Empty;
				JumpTo(nextId);
			}

			return;
		}

		if (_isLineTransitionActive)
		{
			_lineTransitionTimeRemaining -= delta;

			if (_lineTransitionTimeRemaining <= 0.0)
			{
				string targetId = _lineTransitionTargetId;
				_isLineTransitionActive = false;
				_lineTransitionTimeRemaining = 0.0;
				_lineTransitionTargetId = string.Empty;
				JumpTo(targetId);
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

	public override void _Input(InputEvent @event)
	{
		if (_fishingEventBlocking ||
			_fishingTutorialBlocking ||
			_isSilenceActive ||
			_isLineTransitionActive)
		{
			return;
		}

		// 如果现在正在显示选项，
		// 普通鼠标点击不能直接推进剧情。
		if (_choiceCard.Visible)
		{
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

		BeginLineTransition(currentLine.NextId);
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

		EnterFishingTriggerWindow(line.Id);
		ApplyPresentation(line);
		ApplyDialogueEffects(line.Effects);
		ApplyFishingDirective(line);
		ApplyFishingTutorialDirective(line);

		switch (line.Type)
		{
			case DialogueLineType.Dialogue:
				ShowDialogue(line);
				break;

			case DialogueLineType.Thought:
				ShowThought(line);
				break;

			case DialogueLineType.Narration:
				ShowNarration(line);
				break;

			case DialogueLineType.Monologue:
				ShowMonologue(line);
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

		// Fishing now shares the same full-resolution stage as dialogue.
		// Character staging will be added as independent HD cutouts rather
		// than reviving the removed sprite sheet scene.
		ShowIllustration("act1.deck.morning");
	}

	private void ConfigureFishingTriggerWindows()
	{
		DialogueFishingTriggerWindow[] windows =
			DialogueFishingTriggerWindowLoader.Load(
				"res://Data/Fishing/dialogue_trigger_windows.json",
				out string[] loadErrors
			);

		foreach (string error in loadErrors)
		{
			GD.PushError(error);
		}

		if (loadErrors.Length > 0)
		{
			return;
		}

		foreach (DialogueFishingTriggerWindow window in windows)
		{
			if (!_dialogueGraph.TryGetLineIndex(
				window.StartId,
				out int startIndex))
			{
				GD.PushError(
					$"钓鱼触发范围 {window.Id} 的 startId 不存在：" +
					window.StartId
				);
				continue;
			}

			if (!_dialogueGraph.TryGetLineIndex(
				window.EndId,
				out int endIndex) ||
				!_dialogueGraph.TryGetLine(
					window.EndId,
					out DialogueLine endLine))
			{
				GD.PushError(
					$"钓鱼触发范围 {window.Id} 的 endId 不存在：" +
					window.EndId
				);
				continue;
			}

			if (startIndex > endIndex)
			{
				GD.PushError(
					$"钓鱼触发范围 {window.Id} 的 startId 位于 endId 之后。"
				);
				continue;
			}

			if (endLine.Type != DialogueLineType.Silence)
			{
				GD.PushError(
					$"钓鱼触发范围 {window.Id} 必须在沉默节点结束。"
				);
				continue;
			}

			_fishingTriggerWindowsByStartId.Add(
				window.StartId,
				window
			);
		}
	}

	private void EnterFishingTriggerWindow(string lineId)
	{
		if (!_fishingTriggerWindowsByStartId.TryGetValue(
			lineId,
			out DialogueFishingTriggerWindow window) ||
			_completedFishingTriggerWindowIds.Contains(window.Id))
		{
			return;
		}

		if (_activeFishingTriggerWindow != null)
		{
			GD.PushWarning(
				$"钓鱼触发范围 {window.Id} 与尚未结束的范围 " +
				$"{_activeFishingTriggerWindow.Id} 重叠。"
			);
			return;
		}

		_activeFishingTriggerWindow = window;
		_fishingTriggerTimeRemaining =
			window.MinDelaySeconds +
			Random.Shared.NextDouble() *
			(window.MaxDelaySeconds - window.MinDelaySeconds);
	}

	private void UpdateFishingTriggerWindow(double delta)
	{
		if (_activeFishingTriggerWindow == null)
		{
			return;
		}

		_fishingTriggerTimeRemaining -= delta;

		if (_fishingTriggerTimeRemaining <= 0.0)
		{
			TriggerActiveFishingWindow();
		}
	}

	private void TriggerActiveFishingWindow()
	{
		if (_activeFishingTriggerWindow == null)
		{
			return;
		}

		DialogueFishingTriggerWindow window =
			_activeFishingTriggerWindow;
		_activeFishingTriggerWindow = null;
		_fishingTriggerTimeRemaining = 0.0;
		_completedFishingTriggerWindowIds.Add(window.Id);

		FishingEventRequest request =
			_fishingDataStore.CreateEventRequest(
				window.Id,
				_gameState.CurrentFishingSetup
			);

		bool eventStarted =
			_fishingModule.TriggerFishingEvent(request);

		if (!eventStarted)
		{
			GD.PushWarning(
				$"钓鱼触发范围 {window.Id} 到时，但钓鱼模块未能启动事件。"
			);
		}
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
		RememberSpokenSpeaker(line);

		_dialogueTitle.Visible =
			!string.IsNullOrWhiteSpace(line.Speaker);

		_dialogueTitle.Text =
			line.Speaker ?? string.Empty;

		_dialogueText.Text =
			string.Empty;

		StartTypewriter(
			_dialogueText,
			line.Text ?? string.Empty
		);
	}

	private void ShowThought(DialogueLine line)
	{
		ShowDialogueCard();

		_dialogueTitle.Visible = false;

		StartTypewriter(
			_dialogueText,
			$"（{line.Text ?? string.Empty}）"
		);
	}

	private void ShowNarration(DialogueLine line)
	{
		ShowDialogueCard();

		_dialogueTitle.Visible = false;

		StartTypewriter(
			_dialogueText,
			line.Text ?? string.Empty
		);
	}

	private void ShowMonologue(DialogueLine line)
	{
		ShowDialogueCard();
		RememberSpokenSpeaker(line);
		_dialogueTitle.Visible = false;

		StartTypewriter(
			_dialogueText,
			line.Text ?? string.Empty,
			line.Pace
		);
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

		_epigraphLabel.Text =
			EscapeBbcode(line.Text ?? string.Empty);
		_epigraphLabel.VisibleCharacters = -1;
		_epigraphLabel.SelfModulate = Colors.White;
		_epigraphLabel.OffsetTransformPosition =
			Vector2.Zero;
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
		button.Text = choice.Text ?? string.Empty;

		button.Pressed += () => SelectChoice(choice);

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

	private void SelectChoice(DialogueChoice choice)
	{
		_choiceCard.Visible = false;
		ApplyDialogueEffects(choice.Effects);

		// 不再通过数字行号跳转，
		// 而是通过剧情 ID 跳转。
		BeginLineTransition(choice.TargetId);
	}

	private void BeginLineTransition(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
		{
			GD.PushError("尝试安排到一个空的剧情 ID。");
			return;
		}

		if (_dialogueGraph == null ||
			!_dialogueGraph.TryGetLine(targetId, out DialogueLine targetLine))
		{
			GD.PushError($"找不到剧情 ID：{targetId}");
			return;
		}

		// Silence 节点自身就是话题间隔，避免额外叠加文本框停顿。
		if (targetLine.Type == DialogueLineType.Silence)
		{
			JumpTo(targetId);
			return;
		}

		CancelTypewriter();
		_epigraphText.Visible = false;
		_dialogueCard.Visible = false;
		_choiceCard.Visible = false;

		_isLineTransitionActive = true;
		_lineTransitionTimeRemaining =
			GetLineTransitionDelay(targetLine);
		_lineTransitionTargetId = targetId;
	}

	private double GetLineTransitionDelay(DialogueLine targetLine)
	{
		string targetSpeaker = targetLine.Speaker ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(_lastSpokenSpeaker) &&
			!string.IsNullOrWhiteSpace(targetSpeaker) &&
			!string.Equals(
				_lastSpokenSpeaker,
				targetSpeaker,
				StringComparison.Ordinal))
		{
			return SpeakerChangeTransitionDelay;
		}

		return SameSpeakerTransitionDelay;
	}

	private void RememberSpokenSpeaker(DialogueLine line)
	{
		if (!string.IsNullOrWhiteSpace(line.Speaker))
		{
			_lastSpokenSpeaker = line.Speaker;
		}
	}

	private void JumpTo(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
		{
			GD.PushError("尝试跳转到一个空的剧情 ID。");
			return;
		}

		if (_dialogueGraph != null &&
			_dialogueGraph.TryGetLine(
				targetId,
				out DialogueLine _))
		{
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
				break;

			case FishingDirective.Trigger:
				FishingEventRequest request =
					_fishingDataStore.CreateEventRequest(
						line.Id,
						_gameState.CurrentFishingSetup
					);
				bool eventStarted =
					_fishingModule.TriggerFishingEvent(request);

				if (!eventStarted)
				{
					GD.PushWarning(
						$"剧情 {line.Id} 请求触发钓鱼，" +
						"但钓鱼模块尚未启用或事件仍在进行。"
					);
				}
				break;

			case FishingDirective.Disable:
				_fishingEventBlocking = false;
				_fishingModule.SetFishingEnabled(false);
				break;
		}
	}

	private void OnFishingEventFinished(FishingResult result)
	{
		_gameState.ApplyFishingResult(result);
	}

	private void OnFishingInteractionBlockingChanged(bool blocking)
	{
		_fishingEventBlocking = blocking;
	}

	private void OnFishingTutorialStepFinished(
		FishingTutorialStep step,
		FishingSetup setup)
	{
		_gameState.CurrentFishingSetup = setup ?? new FishingSetup();
		_fishingTutorialBlocking = false;
		AdvanceDialogue();
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
		DialoguePace pace = DialoguePace.Normal)
	{
		CancelTypewriter();

		_activeSubtitle = subtitle;
		_activeSubtitleText = text ?? string.Empty;
		_visibleSubtitleCharacters = 0;
		_timeUntilNextCharacter = 0.0;
		_isTypingSubtitle = true;
		_isSubtitleAccelerated = false;
		_activeCharactersPerSecond = pace == DialoguePace.Slow
			? SlowCharactersPerSecond
			: CharactersPerSecond;

		subtitle.Text =
			EscapeBbcode(_activeSubtitleText);

		subtitle.VisibleCharacters = 0;
		_totalSubtitleCharacters =
			subtitle.GetTotalCharacterCount();

		subtitle.SelfModulate =
			new Color(1.0f, 1.0f, 1.0f, 0.0f);
		subtitle.OffsetTransformPosition =
			new Vector2(0.0f, 2.0f);

		_subtitleEntranceTween = CreateTween();
		_subtitleEntranceTween
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.Out)
			.SetParallel(true);
		_subtitleEntranceTween.TweenProperty(
			subtitle,
			"self_modulate:a",
			1.0f,
			SubtitleEntranceDuration
		);
		_subtitleEntranceTween.TweenProperty(
			subtitle,
			"offset_transform_position",
			Vector2.Zero,
			SubtitleEntranceDuration
		);

		if (_totalSubtitleCharacters == 0)
		{
			FinishTypewriter();
		}
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

		if (_subtitleEntranceTween != null)
		{
			_subtitleEntranceTween.Kill();
			_subtitleEntranceTween = null;
		}
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
