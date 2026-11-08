using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class DialogueLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		Converters =
		{
			new JsonStringEnumConverter()
		}
	};

	public static DialogueLine[] Load(params string[] paths)
	{
		List<DialogueLine> lines = new();

		foreach (string path in paths ?? Array.Empty<string>())
		{
			lines.AddRange(LoadFile(path));
		}

		return lines.ToArray();
	}

	private static DialogueLine[] LoadFile(string path)
	{
		if (!FileAccess.FileExists(path))
		{
			GD.PushError($"找不到剧情文件：{path}");
			return Array.Empty<DialogueLine>();
		}

		string json = FileAccess.GetFileAsString(path);

		if (string.IsNullOrWhiteSpace(json))
		{
			GD.PushError($"剧情文件为空：{path}");
			return Array.Empty<DialogueLine>();
		}

		try
		{
			DialogueLine[] lines =
				JsonSerializer.Deserialize<DialogueLine[]>(
					json,
					JsonOptions
				);

			return lines ?? Array.Empty<DialogueLine>();
		}
		catch (JsonException exception)
		{
			GD.PushError(
				$"解析剧情 JSON 失败：{path}\n{exception.Message}"
			);

			return Array.Empty<DialogueLine>();
		}
	}
}
