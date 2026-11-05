using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class FishingDataRepository : IFishingDataRepository
{
	public const string FishCatalogPath =
		"res://Data/Fishing/fish_species.json";
	public const string ProfilesPath =
		"res://Data/Fishing/profiles.json";
	public const string ScriptedEventsPath =
		"res://Data/Fishing/scripted_events.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters =
		{
			new JsonStringEnumConverter()
		}
	};

	private readonly Dictionary<string, FishDefinition> _fishById;
	private readonly Dictionary<string, FishVariantDefinition> _variantsById;
	private readonly Dictionary<string, FishingBaitDefinition> _baitsById;
	private readonly Dictionary<string, FishingDepthDefinition> _depthsById;
	private readonly Dictionary<FishingDepth, string> _depthIdByValue;
	private readonly Dictionary<FishCatchDifficulty, int> _difficultyWeights;
	private readonly int _standardFormWeight;
	private readonly Dictionary<string, BiteProfileDefinition> _biteById;
	private readonly Dictionary<string, FightProfileDefinition> _fightById;
	private readonly Dictionary<string, FishingEventDefinition> _eventsById;

	private FishingDataRepository(
		Dictionary<string, FishDefinition> fishById,
		Dictionary<string, FishVariantDefinition> variantsById,
		Dictionary<string, FishingBaitDefinition> baitsById,
		Dictionary<string, FishingDepthDefinition> depthsById,
		Dictionary<FishingDepth, string> depthIdByValue,
		Dictionary<FishCatchDifficulty, int> difficultyWeights,
		int standardFormWeight,
		Dictionary<string, BiteProfileDefinition> biteById,
		Dictionary<string, FightProfileDefinition> fightById,
		Dictionary<string, FishingEventDefinition> eventsById)
	{
		_fishById = fishById;
		_variantsById = variantsById;
		_baitsById = baitsById;
		_depthsById = depthsById;
		_depthIdByValue = depthIdByValue;
		_difficultyWeights = difficultyWeights;
		_standardFormWeight = standardFormWeight;
		_biteById = biteById;
		_fightById = fightById;
		_eventsById = eventsById;
	}

	public IReadOnlyCollection<FishDefinition> Fish => _fishById.Values;
	public IReadOnlyCollection<FishingBaitDefinition> Baits => _baitsById.Values;
	public IReadOnlyCollection<FishingDepthDefinition> Depths => _depthsById.Values;
	public IReadOnlyCollection<BiteProfileDefinition> BiteProfiles =>
		_biteById.Values;
	public IReadOnlyCollection<FightProfileDefinition> FightProfiles =>
		_fightById.Values;
	public IReadOnlyCollection<FishingEventDefinition> ScriptedEvents =>
		_eventsById.Values;

	public static FishingDataRepository Empty { get; } = new(
		new Dictionary<string, FishDefinition>(StringComparer.Ordinal),
		new Dictionary<string, FishVariantDefinition>(StringComparer.Ordinal),
		new Dictionary<string, FishingBaitDefinition>(StringComparer.Ordinal),
		new Dictionary<string, FishingDepthDefinition>(StringComparer.Ordinal),
		new Dictionary<FishingDepth, string>(),
		new Dictionary<FishCatchDifficulty, int>(),
		0,
		new Dictionary<string, BiteProfileDefinition>(StringComparer.Ordinal),
		new Dictionary<string, FightProfileDefinition>(StringComparer.Ordinal),
		new Dictionary<string, FishingEventDefinition>(StringComparer.Ordinal)
	);

	public bool TryGetFish(string id, out FishDefinition fish)
	{
		return _fishById.TryGetValue(id ?? "", out fish);
	}

	public bool TryGetVariant(
		string id,
		out FishVariantDefinition variant)
	{
		return _variantsById.TryGetValue(id ?? "", out variant);
	}

	public bool TryGetBait(string id, out FishingBaitDefinition bait)
	{
		return _baitsById.TryGetValue(id ?? "", out bait);
	}

	public IReadOnlyList<FishingCatchCandidate> GetCatchCandidates(
		FishingSetup setup)
	{
		List<FishingCatchCandidate> candidates = new();

		if (setup == null ||
			!_baitsById.ContainsKey(setup.BaitId ?? "") ||
			!_depthIdByValue.TryGetValue(setup.Depth, out string depthId))
		{
			return candidates;
		}

		foreach (FishDefinition fish in _fishById.Values)
		{
			if (!ContainsId(fish.Availability.DepthIds, depthId))
			{
				continue;
			}

			if (!ContainsId(fish.Availability.BaitIds, setup.BaitId))
			{
				continue;
			}

			if (_difficultyWeights.TryGetValue(
				fish.CatchDifficulty,
				out int weight) && weight > 0)
			{
				candidates.Add(new FishingCatchCandidate
				{
					Fish = fish,
					Weight = weight
				});
			}
		}

		return candidates;
	}

	public bool TryGetBiteProfile(
		string id,
		out BiteProfileDefinition profile)
	{
		return _biteById.TryGetValue(id ?? "", out profile);
	}

	public bool TryGetFightProfile(
		string id,
		out FightProfileDefinition profile)
	{
		return _fightById.TryGetValue(id ?? "", out profile);
	}

	public bool TryGetScriptedEvent(
		string id,
		out FishingEventDefinition fishingEvent)
	{
		return _eventsById.TryGetValue(id ?? "", out fishingEvent);
	}

	public FishingEventRequest CreateEventRequest(string eventId)
	{
		return CreateEventRequest(eventId, null, Random.Shared);
	}

	public FishingEventRequest CreateEventRequest(
		string eventId,
		FishingSetup setup)
	{
		return CreateEventRequest(eventId, setup, Random.Shared);
	}

	internal FishingEventRequest CreateEventRequest(
		string eventId,
		FishingSetup setup,
		Random random)
	{
		if (TryGetScriptedEvent(eventId, out FishingEventDefinition definition))
		{
			FishingEventRequest request = new()
			{
				EventId = definition.Id,
				BaseFishId = definition.FishId,
				FishId = definition.FishId,
				Quantity = definition.Quantity
			};

			if (TryGetFish(definition.FishId, out FishDefinition fish) &&
				!string.IsNullOrWhiteSpace(fish.BiteProfileId) &&
				TryGetBiteProfile(
					fish.BiteProfileId,
					out BiteProfileDefinition biteProfile
				))
			{
				request.BitePattern = biteProfile.Pattern;
			}

			if (TryGetFish(definition.FishId, out FishDefinition scriptedFish))
				ApplyFightProfile(request, scriptedFish);
			return request;
		}

		IReadOnlyList<FishingCatchCandidate> candidates =
			GetCatchCandidates(setup);
		FishDefinition selectedFish = SelectWeightedFish(candidates, random);

		if (selectedFish != null)
		{
			return CreateNaturalRequest(eventId, selectedFish, random);
		}

		return new FishingEventRequest
		{
			EventId = eventId ?? ""
		};
	}

	private FishingEventRequest CreateNaturalRequest(
		string eventId,
		FishDefinition fish,
		Random random)
	{
		FishVariantDefinition variant = SelectVariant(fish, random);
		FishingEventRequest request = new()
		{
			EventId = eventId ?? "",
			BaseFishId = fish.Id,
			FishId = variant?.Id ?? fish.Id,
			IsSpecialVariant = variant != null,
			Quantity = 1
		};

		if (!string.IsNullOrWhiteSpace(fish.BiteProfileId) &&
			TryGetBiteProfile(
				fish.BiteProfileId,
				out BiteProfileDefinition biteProfile))
		{
			request.BitePattern = biteProfile.Pattern;
		}

		ApplyFightProfile(request, fish);
		return request;
	}

	private void ApplyFightProfile(FishingEventRequest request, FishDefinition fish)
	{
		if (TryGetFightProfile(fish.FightProfileId, out FightProfileDefinition profile))
		{
			request.FightProfileId = profile.Id;
			request.FightMovement = profile.Movement.Copy();
		}
	}

	private static FishDefinition SelectWeightedFish(
		IReadOnlyList<FishingCatchCandidate> candidates,
		Random random)
	{
		int totalWeight = 0;

		foreach (FishingCatchCandidate candidate in candidates)
		{
			totalWeight += candidate.Weight;
		}

		if (totalWeight <= 0)
		{
			return null;
		}

		int roll = random.Next(totalWeight);

		foreach (FishingCatchCandidate candidate in candidates)
		{
			if (roll < candidate.Weight)
			{
				return candidate.Fish;
			}

			roll -= candidate.Weight;
		}

		return null;
	}

	private FishVariantDefinition SelectVariant(
		FishDefinition fish,
		Random random)
	{
		int totalWeight = _standardFormWeight;

		foreach (FishVariantDefinition variant in fish.Variants)
		{
			totalWeight += variant.OccurrenceWeight;
		}

		int roll = random.Next(totalWeight);

		if (roll < _standardFormWeight)
		{
			return null;
		}

		roll -= _standardFormWeight;

		foreach (FishVariantDefinition variant in fish.Variants)
		{
			if (roll < variant.OccurrenceWeight)
			{
				return variant;
			}

			roll -= variant.OccurrenceWeight;
		}

		return null;
	}

	private static bool ContainsId(string[] ids, string targetId)
	{
		return Array.IndexOf(ids ?? Array.Empty<string>(), targetId) >= 0;
	}

	public static bool TryLoad(
		out FishingDataRepository repository,
		out string[] errors)
	{
		List<string> validationErrors = new();

		FishCatalogDocument fishDocument = LoadDocument<FishCatalogDocument>(
			FishCatalogPath,
			validationErrors
		);
		FishingProfilesDocument profilesDocument =
			LoadDocument<FishingProfilesDocument>(
				ProfilesPath,
				validationErrors
			);
		FishingEventsDocument eventsDocument =
			LoadDocument<FishingEventsDocument>(
				ScriptedEventsPath,
				validationErrors
			);

		Dictionary<string, FishDefinition> fishById =
			new(StringComparer.Ordinal);
		Dictionary<string, FishVariantDefinition> variantsById =
			new(StringComparer.Ordinal);
		Dictionary<string, FishingBaitDefinition> baitsById =
			new(StringComparer.Ordinal);
		Dictionary<string, FishingDepthDefinition> depthsById =
			new(StringComparer.Ordinal);
		Dictionary<FishingDepth, string> depthIdByValue = new();
		Dictionary<FishCatchDifficulty, int> difficultyWeights = new();
		Dictionary<string, BiteProfileDefinition> biteById =
			new(StringComparer.Ordinal);
		Dictionary<string, FightProfileDefinition> fightById =
			new(StringComparer.Ordinal);
		Dictionary<string, FishingEventDefinition> eventsById =
			new(StringComparer.Ordinal);

		ValidateSchemaVersion(
			FishCatalogPath,
			fishDocument.SchemaVersion,
			validationErrors
		);
		ValidateSchemaVersion(
			ProfilesPath,
			profilesDocument.SchemaVersion,
			validationErrors
		);
		ValidateSchemaVersion(
			ScriptedEventsPath,
			eventsDocument.SchemaVersion,
			validationErrors
		);

		IndexBaits(fishDocument.Baits, baitsById, validationErrors);
		IndexDepths(
			fishDocument.Depths,
			depthsById,
			depthIdByValue,
			validationErrors
		);
		ValidateSelectionRules(
			fishDocument.SelectionRules,
			difficultyWeights,
			validationErrors
		);
		IndexFish(
			fishDocument.Fish,
			fishById,
			variantsById,
			baitsById,
			depthsById,
			validationErrors
		);
		IndexBiteProfiles(
			profilesDocument.BiteProfiles,
			biteById,
			validationErrors
		);
		IndexFightProfiles(
			profilesDocument.FightProfiles,
			fightById,
			validationErrors
		);
		ValidateFishProfiles(
			fishById.Values,
			biteById,
			fightById,
			validationErrors
		);
		IndexEvents(
			eventsDocument.Events,
			fishById,
			eventsById,
			validationErrors
		);

		if (validationErrors.Count > 0)
		{
			repository = Empty;
			errors = validationErrors.ToArray();
			return false;
		}

		repository = new FishingDataRepository(
			fishById,
			variantsById,
			baitsById,
			depthsById,
			depthIdByValue,
			difficultyWeights,
			fishDocument.SelectionRules.StandardFormWeight,
			biteById,
			fightById,
			eventsById
		);
		errors = Array.Empty<string>();
		return true;
	}

	private static T LoadDocument<T>(
		string path,
		List<string> errors)
		where T : class, new()
	{
		if (!FileAccess.FileExists(path))
		{
			errors.Add($"找不到钓鱼数据文件：{path}");
			return new T();
		}

		string json = FileAccess.GetFileAsString(path);

		if (string.IsNullOrWhiteSpace(json))
		{
			errors.Add($"钓鱼数据文件为空：{path}");
			return new T();
		}

		try
		{
			return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
		}
		catch (JsonException exception)
		{
			errors.Add($"解析钓鱼数据失败：{path}\n{exception.Message}");
			return new T();
		}
	}

	private static void ValidateSchemaVersion(
		string path,
		int schemaVersion,
		List<string> errors)
	{
		if (schemaVersion != 1)
		{
			errors.Add(
				$"钓鱼数据 {path} 使用不支持的 schemaVersion：" +
				schemaVersion
			);
		}
	}

	private static void IndexFish(
		FishDefinition[] fishDefinitions,
		Dictionary<string, FishDefinition> fishById,
		Dictionary<string, FishVariantDefinition> variantsById,
		Dictionary<string, FishingBaitDefinition> baitsById,
		Dictionary<string, FishingDepthDefinition> depthsById,
		List<string> errors)
	{
		HashSet<string> catchIds = new(StringComparer.Ordinal);

		foreach (FishDefinition fish in
			fishDefinitions ?? Array.Empty<FishDefinition>())
		{
			if (fish == null)
			{
				errors.Add("fish_species.json 包含 null 鱼种。");
				continue;
			}

			if (!ValidateStableId(fish.Id, "鱼种", errors) ||
				!catchIds.Add(fish.Id))
			{
				if (!string.IsNullOrWhiteSpace(fish.Id))
				{
					errors.Add($"发现重复的鱼种或变种 ID：{fish.Id}");
				}

				continue;
			}

			if (string.IsNullOrWhiteSpace(fish.DisplayName))
			{
				errors.Add($"鱼种 {fish.Id} 缺少 displayName。");
			}

			fish.Availability ??= new FishAvailabilityDefinition();
			ValidateStringIds(
				fish.Availability.BaitIds,
				$"鱼种 {fish.Id} 的 baitIds",
				errors
			);
			ValidateReferences(
				fish.Availability.BaitIds,
				baitsById,
				$"鱼种 {fish.Id} 的饵料",
				errors
			);
			ValidateStringIds(
				fish.Availability.DepthIds,
				$"鱼种 {fish.Id} 的 depthIds",
				errors
			);
			ValidateReferences(
				fish.Availability.DepthIds,
				depthsById,
				$"鱼种 {fish.Id} 的水层",
				errors
			);
			ValidateStringIds(
				fish.Availability.WeatherIds,
				$"鱼种 {fish.Id} 的 weatherIds",
				errors
			);
			ValidateStringIds(
				fish.Availability.WindIds,
				$"鱼种 {fish.Id} 的 windIds",
				errors
			);
			ValidateStringIds(
				fish.Availability.TimeIds,
				$"鱼种 {fish.Id} 的 timeIds",
				errors
			);
			ValidateStringIds(
				fish.Availability.AreaIds,
				$"鱼种 {fish.Id} 的 areaIds",
				errors
			);

			foreach (FishVariantDefinition variant in
				fish.Variants ?? Array.Empty<FishVariantDefinition>())
			{
				if (variant == null)
				{
					errors.Add($"鱼种 {fish.Id} 包含 null 变种。");
					continue;
				}

				if (!ValidateStableId(variant.Id, "鱼种变种", errors) ||
					!catchIds.Add(variant.Id))
				{
					if (!string.IsNullOrWhiteSpace(variant.Id))
					{
						errors.Add(
							$"发现重复的鱼种或变种 ID：{variant.Id}"
						);
					}

					continue;
				}

				if (string.IsNullOrWhiteSpace(variant.DisplayName))
				{
					errors.Add($"鱼种变种 {variant.Id} 缺少 displayName。");
				}

				if (variant.OccurrenceWeight < 1)
				{
					errors.Add(
						$"鱼种变种 {variant.Id} 的 occurrenceWeight " +
						"必须大于 0。"
					);
				}

				variantsById.TryAdd(variant.Id, variant);
			}

			bool hasValidBait = HasValidReference(
				fish.Availability.BaitIds,
				baitsById
			);
			bool hasValidDepth = HasValidReference(
				fish.Availability.DepthIds,
				depthsById
			);

			if (!hasValidBait || !hasValidDepth)
			{
				errors.Add(
					$"鱼种 {fish.Id} 没有任何可捕获的饵料与水层组合。"
				);
			}

			fishById.Add(fish.Id, fish);
		}
	}

	private static void IndexBaits(
		FishingBaitDefinition[] baits,
		Dictionary<string, FishingBaitDefinition> baitsById,
		List<string> errors)
	{
		foreach (FishingBaitDefinition bait in
			baits ?? Array.Empty<FishingBaitDefinition>())
		{
			if (bait == null)
			{
				errors.Add("fish_species.json 包含 null 饵料。");
				continue;
			}

			if (!ValidateStableId(bait.Id, "饵料", errors))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(bait.DisplayName))
			{
				errors.Add($"饵料 {bait.Id} 缺少 displayName。");
			}

			if (!baitsById.TryAdd(bait.Id, bait))
			{
				errors.Add($"发现重复的饵料 ID：{bait.Id}");
			}
		}
	}

	private static void IndexDepths(
		FishingDepthDefinition[] depths,
		Dictionary<string, FishingDepthDefinition> depthsById,
		Dictionary<FishingDepth, string> depthIdByValue,
		List<string> errors)
	{
		foreach (FishingDepthDefinition depth in
			depths ?? Array.Empty<FishingDepthDefinition>())
		{
			if (depth == null)
			{
				errors.Add("fish_species.json 包含 null 水层。");
				continue;
			}

			if (!ValidateStableId(depth.Id, "水层", errors))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(depth.DisplayName))
			{
				errors.Add($"水层 {depth.Id} 缺少 displayName。");
			}

			if (!depthsById.TryAdd(depth.Id, depth))
			{
				errors.Add($"发现重复的水层 ID：{depth.Id}");
			}

			if (!depthIdByValue.TryAdd(depth.Value, depth.Id))
			{
				errors.Add($"水层枚举值 {depth.Value} 被重复定义。");
			}
		}

		foreach (FishingDepth value in Enum.GetValues<FishingDepth>())
		{
			if (!depthIdByValue.ContainsKey(value))
			{
				errors.Add($"缺少水层枚举值 {value} 的数据定义。");
			}
		}
	}

	private static void ValidateSelectionRules(
		FishingSelectionRulesDefinition rules,
		Dictionary<FishCatchDifficulty, int> difficultyWeights,
		List<string> errors)
	{
		if (rules == null)
		{
			errors.Add("fish_species.json 缺少 selectionRules。");
			return;
		}

		if (rules.StandardFormWeight < 1)
		{
			errors.Add("selectionRules.standardFormWeight 必须大于 0。");
		}

		foreach (FishingDifficultyWeightDefinition definition in
			rules.DifficultyWeights ??
			Array.Empty<FishingDifficultyWeightDefinition>())
		{
			if (definition == null)
			{
				errors.Add("selectionRules 包含 null 难度权重。");
				continue;
			}

			if (definition.Weight < 1)
			{
				errors.Add(
					$"获取难度 {definition.Difficulty} 的权重必须大于 0。"
				);
			}

			if (!difficultyWeights.TryAdd(
				definition.Difficulty,
				definition.Weight))
			{
				errors.Add(
					$"获取难度 {definition.Difficulty} 被重复定义。"
				);
			}
		}

		foreach (FishCatchDifficulty difficulty in
			Enum.GetValues<FishCatchDifficulty>())
		{
			if (!difficultyWeights.ContainsKey(difficulty))
			{
				errors.Add($"缺少获取难度 {difficulty} 的权重定义。");
			}
		}
	}

	private static void IndexBiteProfiles(
		BiteProfileDefinition[] profiles,
		Dictionary<string, BiteProfileDefinition> profilesById,
		List<string> errors)
	{
		foreach (BiteProfileDefinition profile in
			profiles ?? Array.Empty<BiteProfileDefinition>())
		{
			if (profile == null)
			{
				errors.Add("profiles.json 包含 null 咬钩模板。");
				continue;
			}

			if (!ValidateStableId(profile.Id, "咬钩模板", errors))
			{
				continue;
			}

			if (!profilesById.TryAdd(profile.Id, profile))
			{
				errors.Add($"发现重复的咬钩模板 ID：{profile.Id}");
			}
		}
	}

	private static void IndexFightProfiles(
		FightProfileDefinition[] profiles,
		Dictionary<string, FightProfileDefinition> profilesById,
		List<string> errors)
	{
		foreach (FightProfileDefinition profile in
			profiles ?? Array.Empty<FightProfileDefinition>())
		{
			if (profile == null)
			{
				errors.Add("profiles.json 包含 null 搏鱼模板。");
				continue;
			}

			if (!ValidateStableId(profile.Id, "搏鱼模板", errors))
			{
				continue;
			}

			if (profile.Movement == null)
				errors.Add($"Fight profile {profile.Id} is missing movement settings.");
			else
				foreach (string error in profile.Movement.ValidationErrors())
					errors.Add($"Fight profile {profile.Id}: {error}");

			if (!profilesById.TryAdd(profile.Id, profile))
			{
				errors.Add($"发现重复的搏鱼模板 ID：{profile.Id}");
			}
		}
	}

	private static void ValidateFishProfiles(
		Dictionary<string, FishDefinition>.ValueCollection fish,
		Dictionary<string, BiteProfileDefinition> biteProfiles,
		Dictionary<string, FightProfileDefinition> fightProfiles,
		List<string> errors)
	{
		foreach (FishDefinition definition in fish)
		{
			if (string.IsNullOrWhiteSpace(definition.BiteProfileId))
			{
				errors.Add($"鱼种 {definition.Id} 缺少咬钩模板。");
			}
			else if (!biteProfiles.TryGetValue(
				definition.BiteProfileId,
				out BiteProfileDefinition biteProfile))
			{
				errors.Add(
					$"鱼种 {definition.Id} 引用了不存在的咬钩模板：" +
					definition.BiteProfileId
				);
			}
			else if (definition.CatchDifficulty == FishCatchDifficulty.Low &&
				biteProfile.Pattern == FishBitePattern.Drag)
			{
				errors.Add(
					$"低难度鱼种 {definition.Id} 不能使用拖饵型咬钩模板。"
				);
			}

			if (!string.IsNullOrWhiteSpace(definition.FightProfileId) &&
				!fightProfiles.ContainsKey(definition.FightProfileId))
			{
				errors.Add(
					$"鱼种 {definition.Id} 引用了不存在的搏鱼模板：" +
					definition.FightProfileId
				);
			}
		}
	}

	private static void IndexEvents(
		FishingEventDefinition[] events,
		Dictionary<string, FishDefinition> fishById,
		Dictionary<string, FishingEventDefinition> eventsById,
		List<string> errors)
	{
		foreach (FishingEventDefinition fishingEvent in
			events ?? Array.Empty<FishingEventDefinition>())
		{
			if (fishingEvent == null)
			{
				errors.Add("scripted_events.json 包含 null 事件。");
				continue;
			}

			if (!ValidateStableId(fishingEvent.Id, "钓鱼事件", errors))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(fishingEvent.FishId) ||
				!fishById.ContainsKey(fishingEvent.FishId))
			{
				errors.Add(
					$"钓鱼事件 {fishingEvent.Id} 引用了不存在的基础鱼种：" +
					fishingEvent.FishId
				);
			}

			if (fishingEvent.Quantity < 1)
			{
				errors.Add($"钓鱼事件 {fishingEvent.Id} 的 quantity 必须大于 0。");
			}

			if (!eventsById.TryAdd(fishingEvent.Id, fishingEvent))
			{
				errors.Add($"发现重复的钓鱼事件 ID：{fishingEvent.Id}");
			}
		}
	}

	private static bool ValidateStableId(
		string id,
		string owner,
		List<string> errors)
	{
		if (!string.IsNullOrWhiteSpace(id))
		{
			return true;
		}

		errors.Add($"{owner} 缺少稳定 ID。");
		return false;
	}

	private static void ValidateStringIds(
		string[] ids,
		string owner,
		List<string> errors)
	{
		foreach (string id in ids ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				errors.Add($"{owner} 包含空 ID。");
			}
		}
	}

	private static void ValidateReferences<T>(
		string[] ids,
		Dictionary<string, T> definitions,
		string owner,
		List<string> errors)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);

		foreach (string id in ids ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				continue;
			}

			if (!seen.Add(id))
			{
				errors.Add($"{owner} 重复引用 ID：{id}");
			}

			if (!definitions.ContainsKey(id))
			{
				errors.Add($"{owner} 引用了不存在的 ID：{id}");
			}
		}
	}

	private static bool HasValidReference<T>(
		string[] ids,
		Dictionary<string, T> definitions)
	{
		foreach (string id in ids ?? Array.Empty<string>())
		{
			if (!string.IsNullOrWhiteSpace(id) && definitions.ContainsKey(id))
			{
				return true;
			}
		}

		return false;
	}
}
