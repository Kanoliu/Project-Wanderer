using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public static class DialogueFishingTriggerWindowLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	public static DialogueFishingTriggerWindow[] Load(
		string path,
		out string[] errors)
	{
		List<string> validationErrors = new();

		if (!FileAccess.FileExists(path))
		{
			errors = new[] { $"找不到对话钓鱼触发范围：{path}" };
			return Array.Empty<DialogueFishingTriggerWindow>();
		}

		try
		{
			DialogueFishingTriggerWindowDocument document =
				JsonSerializer.Deserialize<DialogueFishingTriggerWindowDocument>(
					FileAccess.GetFileAsString(path),
					JsonOptions
				) ?? new DialogueFishingTriggerWindowDocument();

			if (document.SchemaVersion != 1)
			{
				validationErrors.Add(
					$"对话钓鱼触发范围使用不支持的 schemaVersion：" +
					document.SchemaVersion
				);
			}

			Validate(document.Windows, validationErrors);
			errors = validationErrors.ToArray();
			return document.Windows ??
				Array.Empty<DialogueFishingTriggerWindow>();
		}
		catch (JsonException exception)
		{
			errors = new[]
			{
				$"解析对话钓鱼触发范围失败：{path}\n{exception.Message}"
			};
			return Array.Empty<DialogueFishingTriggerWindow>();
		}
	}

	private static void Validate(
		DialogueFishingTriggerWindow[] windows,
		List<string> errors)
	{
		HashSet<string> ids = new(StringComparer.Ordinal);
		HashSet<string> startIds = new(StringComparer.Ordinal);

		foreach (DialogueFishingTriggerWindow window in
			windows ?? Array.Empty<DialogueFishingTriggerWindow>())
		{
			if (window == null)
			{
				errors.Add("对话钓鱼触发范围中包含 null。");
				continue;
			}

			if (string.IsNullOrWhiteSpace(window.Id) ||
				!ids.Add(window.Id))
			{
				errors.Add($"对话钓鱼触发范围 ID 为空或重复：{window.Id}");
			}

			if (string.IsNullOrWhiteSpace(window.StartId) ||
				!startIds.Add(window.StartId))
			{
				errors.Add(
					$"触发范围 {window.Id} 的 startId 为空或重复。"
				);
			}

			if (string.IsNullOrWhiteSpace(window.EndId))
			{
				errors.Add($"触发范围 {window.Id} 缺少 endId。");
			}

			if (window.MinDelaySeconds < 0.0 ||
				window.MaxDelaySeconds < window.MinDelaySeconds)
			{
				errors.Add($"触发范围 {window.Id} 的随机延时无效。");
			}
		}
	}
}
