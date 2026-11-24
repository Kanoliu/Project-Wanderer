using System;
using System.Collections.Generic;

public sealed class DialogueGraph
{
	private readonly Dictionary<string, DialogueLine> _linesById;
	private readonly Dictionary<string, int> _lineIndexById;

	private DialogueGraph(
		Dictionary<string, DialogueLine> linesById,
		Dictionary<string, int> lineIndexById,
		string entryId)
	{
		_linesById = linesById;
		_lineIndexById = lineIndexById;
		EntryId = entryId;
	}

	public string EntryId { get; }

	public bool TryGetLine(string id, out DialogueLine line)
	{
		return _linesById.TryGetValue(id, out line);
	}

	public bool TryGetLineIndex(string id, out int index)
	{
		return _lineIndexById.TryGetValue(id, out index);
	}

	public static bool TryCreate(
		DialogueLine[] lines,
		out DialogueGraph graph,
		out string[] errors)
	{
		List<string> validationErrors = new();
		Dictionary<string, DialogueLine> linesById =
			new(StringComparer.Ordinal);
		Dictionary<string, int> lineIndexById =
			new(StringComparer.Ordinal);

		if (lines == null || lines.Length == 0)
		{
			graph = null;
			errors = new[] { "剧情文件没有可用节点。" };
			return false;
		}

		for (int index = 0; index < lines.Length; index++)
		{
			DialogueLine line = lines[index];

			if (line == null)
			{
				validationErrors.Add($"第 {index} 条剧情是 null。");
				continue;
			}

			if (string.IsNullOrWhiteSpace(line.Id))
			{
				validationErrors.Add($"第 {index} 条剧情没有设置 Id。");
				continue;
			}

			if (!linesById.TryAdd(line.Id, line))
			{
				validationErrors.Add($"发现重复的剧情 ID：{line.Id}");
			}
			else
			{
				lineIndexById.Add(line.Id, index);
			}
		}

		foreach (DialogueLine line in linesById.Values)
		{
			ValidateLine(line, linesById, validationErrors);
		}

		string entryId = lines[0]?.Id ?? "";

		if (!string.IsNullOrWhiteSpace(entryId) &&
			linesById.ContainsKey(entryId))
		{
			ValidateReachability(
				entryId,
				linesById,
				validationErrors
			);
		}

		if (validationErrors.Count > 0)
		{
			graph = null;
			errors = validationErrors.ToArray();
			return false;
		}

		graph = new DialogueGraph(
			linesById,
			lineIndexById,
			entryId
		);
		errors = Array.Empty<string>();
		return true;
	}

	private static void ValidateLine(
		DialogueLine line,
		Dictionary<string, DialogueLine> linesById,
		List<string> errors)
	{
		if (line.Type != DialogueLineType.Choice &&
			line.Type != DialogueLineType.Silence &&
			line.Type != DialogueLineType.FishingTutorial &&
			line.Type != DialogueLineType.DaySettlement &&
			line.Type != DialogueLineType.ChapterTransition &&
			string.IsNullOrWhiteSpace(line.Text))
		{
			errors.Add($"剧情 {line.Id} 的正文为空。");
		}

		if (!string.IsNullOrWhiteSpace(line.NextId) &&
			!linesById.ContainsKey(line.NextId))
		{
			errors.Add(
				$"剧情 {line.Id} 指向不存在的 nextId：{line.NextId}"
			);
		}

		if (line.Type == DialogueLineType.Choice)
		{
			ValidateChoices(line, linesById, errors);
		}

		if (line.Type == DialogueLineType.ChapterTransition &&
			(line.Day is <= 0 || string.IsNullOrWhiteSpace(line.NextId) || line.NextId == line.Id))
		{
			errors.Add($"章节转场 {line.Id} 必须指向下一剧情节点；day 如有设置，必须从 1 开始。");
		}

		bool hasCondition = line.RequiresDinnerFish || !string.IsNullOrWhiteSpace(line.RequiredCatchId);
		if ((hasCondition || !string.IsNullOrWhiteSpace(line.FallbackId)) &&
			(!hasCondition ||
				string.IsNullOrWhiteSpace(line.FallbackId) ||
				line.FallbackId == line.Id || !linesById.ContainsKey(line.FallbackId)))
		{
			errors.Add($"剧情 {line.Id} 的条件跳转必须设置有效条件与 fallbackId。");
		}

		if (line.Type == DialogueLineType.DinnerDialogue &&
			(string.IsNullOrWhiteSpace(line.SpeakerId) || string.IsNullOrWhiteSpace(line.Speaker)))
		{
			errors.Add($"晚餐对白 {line.Id} 必须设置 speakerId 和 speaker。");
		}

		if (line.Type == DialogueLineType.FishingTutorial &&
			(!Enum.IsDefined(line.Tutorial) ||
				line.Tutorial == FishingTutorialStep.None ||
				!string.IsNullOrWhiteSpace(line.Text) ||
				string.IsNullOrWhiteSpace(line.NextId)))
		{
			errors.Add($"钓鱼教学节点 {line.Id} 必须设置有效的 tutorial 和 nextId，且正文应为空。");
		}

		if (line.Type == DialogueLineType.Silence)
		{
			if (line.DurationSeconds < 5.0 ||
				line.DurationSeconds > 20.0)
			{
				errors.Add(
					$"沉默节点 {line.Id} 的时长必须在 5 到 20 秒之间。"
				);
			}

			if (string.IsNullOrWhiteSpace(line.NextId))
			{
				errors.Add($"沉默节点 {line.Id} 必须设置 nextId。");
			}
		}

		ValidateEffects(line.Id, line.Effects, errors);
	}

	private static void ValidateChoices(
		DialogueLine line,
		Dictionary<string, DialogueLine> linesById,
		List<string> errors)
	{
		DialogueChoice[] choices =
			line.Choices ?? Array.Empty<DialogueChoice>();

		if (choices.Length == 0)
		{
			errors.Add($"选择节点 {line.Id} 没有选项。");
			return;
		}

		for (int index = 0; index < choices.Length; index++)
		{
			DialogueChoice choice = choices[index];

			if (choice == null)
			{
				errors.Add($"选择节点 {line.Id} 的第 {index} 个选项是 null。");
				continue;
			}

			if (string.IsNullOrWhiteSpace(choice.Text))
			{
				errors.Add($"选择节点 {line.Id} 的第 {index} 个选项没有文本。");
			}

			if (string.IsNullOrWhiteSpace(choice.TargetId))
			{
				errors.Add($"选择节点 {line.Id} 的第 {index} 个选项没有 targetId。");
			}
			else if (!linesById.ContainsKey(choice.TargetId))
			{
				errors.Add(
					$"选择节点 {line.Id} 指向不存在的 targetId：" +
					choice.TargetId
				);
			}

			ValidateEffects(
				$"{line.Id} 的第 {index} 个选项",
				choice.Effects,
				errors
			);
		}
	}

	private static void ValidateEffects(
		string owner,
		DialogueEffect[] effects,
		List<string> errors)
	{
		foreach (DialogueEffect effect in
			effects ?? Array.Empty<DialogueEffect>())
		{
			if (effect == null)
			{
				errors.Add($"剧情效果 {owner} 中包含 null。");
				continue;
			}

			if (effect.Type != DialogueEffectType.None &&
				string.IsNullOrWhiteSpace(effect.TargetId))
			{
				errors.Add($"剧情效果 {owner} 缺少 targetId。");
			}
		}
	}

	private static void ValidateReachability(
		string entryId,
		Dictionary<string, DialogueLine> linesById,
		List<string> errors)
	{
		HashSet<string> visited = new(StringComparer.Ordinal);
		Stack<string> pending = new();
		pending.Push(entryId);

		while (pending.Count > 0)
		{
			string id = pending.Pop();

			if (!visited.Add(id) ||
				!linesById.TryGetValue(id, out DialogueLine line))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(line.NextId))
			{
				pending.Push(line.NextId);
			}

			if (!string.IsNullOrWhiteSpace(line.FallbackId))
			{
				pending.Push(line.FallbackId);
			}

			foreach (DialogueChoice choice in
				line.Choices ?? Array.Empty<DialogueChoice>())
			{
				if (choice != null &&
					!string.IsNullOrWhiteSpace(choice.TargetId))
				{
					pending.Push(choice.TargetId);
				}
			}
		}

		foreach (string id in linesById.Keys)
		{
			if (!visited.Contains(id))
			{
				errors.Add($"剧情节点无法从入口到达：{id}");
			}
		}
	}
}
