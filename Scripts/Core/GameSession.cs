using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class GameSession : Node
{
	private const string CharacterCatalogPath = "res://Data/Characters/characters.json";
	private static readonly JsonSerializerOptions CharacterJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};
	private readonly Dictionary<string, int> _initialRelationships = new(StringComparer.Ordinal);

	private sealed record CharacterDefinition(string Id, string DisplayName, int? InitialRelationship);

	public GameState State { get; private set; } = new();

	public override void _Ready()
	{
		LoadCharacterDefaults();
		State.InitializeRelationships(_initialRelationships);
	}

	public void StartNewGame()
	{
		LoadState(new GameState());
	}

	public void LoadState(GameState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		state.InitializeRelationships(_initialRelationships);
		State = state;
	}

	private void LoadCharacterDefaults()
	{
		if (!FileAccess.FileExists(CharacterCatalogPath))
		{
			GD.PushError($"找不到角色资料：{CharacterCatalogPath}");
			return;
		}

		CharacterDefinition[] characters;
		try
		{
			characters = JsonSerializer.Deserialize<CharacterDefinition[]>(
				FileAccess.GetFileAsString(CharacterCatalogPath), CharacterJsonOptions
			);
		}
		catch (JsonException exception)
		{
			GD.PushError($"解析角色资料失败：{CharacterCatalogPath}\n{exception.Message}");
			return;
		}

		if (characters == null || characters.Length == 0)
		{
			GD.PushError($"角色资料没有条目：{CharacterCatalogPath}");
			return;
		}

		HashSet<string> characterIds = new(StringComparer.Ordinal);
		foreach (CharacterDefinition character in characters)
		{
			if (character == null || string.IsNullOrWhiteSpace(character.Id) ||
				string.IsNullOrWhiteSpace(character.DisplayName))
			{
				GD.PushError($"角色资料中的条目缺少 ID 或名称：{CharacterCatalogPath}");
				continue;
			}

			if (!characterIds.Add(character.Id))
			{
				GD.PushError($"发现重复的角色 ID：{character.Id}");
				continue;
			}

			if (character.InitialRelationship is int value)
			{
				_initialRelationships.Add(character.Id, value);
			}
		}
	}
}
