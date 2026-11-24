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
	private readonly Dictionary<FishCatchDifficulty, float> _progressLossRates;

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
		Dictionary<string, FishingEventDefinition> eventsById,
		float specialVariantMultiplier,
		float progressCapacityMultiplier,
		Dictionary<FishCatchDifficulty, float> progressLossRates,
		float progressGainRate)
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
		SpecialVariantMultiplier = specialVariantMultiplier;
		ProgressCapacityMultiplier = progressCapacityMultiplier;
		_progressLossRates = progressLossRates;
		ProgressGainRate = progressGainRate;
	}

	public float SpecialVariantMultiplier { get; }
	public float ProgressCapacityMultiplier { get; }
	public float ProgressGainRate { get; }
	public float GetBaseProgressLossRate(FishCatchDifficulty difficulty) => _progressLossRates[difficulty];

	// One shared rule for natural encounters and the per-species test screen.
	public FightProfileDefinition CreateFightProfile(string baseFishId, bool isSpecialVariant = false)
	{
		if (!TryGetFish(baseFishId, out var fish) ||
			!TryGetFightProfile(fish.FightProfileId, out var basis)) return null;
		float strength = isSpecialVariant ? SpecialVariantMultiplier : 1f;
		var movement = basis.Movement.Copy();
		movement.CruiseSpeed *= strength;
		movement.Acceleration *= strength;
		movement.MaxTurnRate *= strength;
		movement.BurstTurnRate *= strength;
		return new FightProfileDefinition
		{
			Id = basis.Id,
			Pattern = basis.Pattern,
			Description = basis.Description,
			Movement = movement,
			GainRate = ProgressGainRate / ProgressCapacityMultiplier,
			LossDifficulty = basis.LossDifficulty,
			LossRate = GetBaseProgressLossRate(basis.LossDifficulty.Value) * strength / ProgressCapacityMultiplier
		};
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
		new Dictionary<string, FishingEventDefinition>(StringComparer.Ordinal),
		1.15f,
		2.5f,
		new Dictionary<FishCatchDifficulty, float>(),
		0.16f
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
		return GetCatchCandidates(setup, FishCatchDifficulty.High);
	}

	private IReadOnlyList<FishingCatchCandidate> GetCatchCandidates(
		FishingSetup setup,
		FishCatchDifficulty maxDifficulty)
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
			if (fish.CatchDifficulty > maxDifficulty ||
				!ContainsId(fish.Availability.DepthIds, depthId))
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
		FishingSetup setup,
		int day = 0)
	{
		return CreateEventRequest(eventId, setup, Random.Shared, day);
	}

	internal FishingEventRequest CreateEventRequest(
		string eventId,
		FishingSetup setup,
		Random random,
		int day = 0)
	{
		FishCatchDifficulty maxDifficulty = day == 1
			? FishCatchDifficulty.Medium
			: FishCatchDifficulty.High;
		if (TryGetScriptedEvent(eventId, out FishingEventDefinition definition) &&
			TryGetFish(definition.FishId, out FishDefinition scriptedFish) &&
			scriptedFish.CatchDifficulty <= maxDifficulty)
		{
			FishingEventRequest request = new()
			{
				EventId = definition.Id,
				BaseFishId = definition.FishId,
				FishId = definition.FishId,
				Quantity = definition.Quantity
			};

			if (!string.IsNullOrWhiteSpace(scriptedFish.BiteProfileId) &&
				TryGetBiteProfile(
					scriptedFish.BiteProfileId,
					out BiteProfileDefinition biteProfile
				))
			{
				request.BitePattern = biteProfile.Pattern;
			}

			ApplyFightProfile(request, scriptedFish);
			return request;
		}

		IReadOnlyList<FishingCatchCandidate> candidates =
			GetCatchCandidates(setup, maxDifficulty);
		FishDefinition selectedFish = SelectWeightedFish(candidates, random);

		if (selectedFish != null)
		{
			return CreateNaturalRequest(eventId, selectedFish, random, day != 1);
		}

		return new FishingEventRequest
		{
			EventId = eventId ?? "",
			FightGainRate = ProgressGainRate / ProgressCapacityMultiplier
		};
	}

	private FishingEventRequest CreateNaturalRequest(
		string eventId,
		FishDefinition fish,
		Random random,
		bool allowVariants)
	{
		FishVariantDefinition variant = allowVariants ? SelectVariant(fish, random) : null;
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
		var profile = CreateFightProfile(fish.Id, request.IsSpecialVariant);
		if (profile != null)
		{
			request.FightProfileId = profile.Id;
			request.FightMovement = profile.Movement.Copy();
			request.FightGainRate = profile.GainRate;
			request.FightLossRate = profile.LossRate;
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
		if (!float.IsFinite(profilesDocument.SpecialVariantMultiplier) ||
			profilesDocument.SpecialVariantMultiplier < 1 || profilesDocument.SpecialVariantMultiplier > 2)
			validationErrors.Add("specialVariantMultiplier 必须在 [1, 2] 范围内。");
		if (!float.IsFinite(profilesDocument.ProgressCapacityMultiplier) ||
			profilesDocument.ProgressCapacityMultiplier < 1 || profilesDocument.ProgressCapacityMultiplier > 10)
			validationErrors.Add("progressCapacityMultiplier 必须在 [1, 10] 范围内。");
		if (!float.IsFinite(profilesDocument.ProgressGainRate) ||
			profilesDocument.ProgressGainRate <= 0 || profilesDocument.ProgressGainRate > 1)
			validationErrors.Add("progressGainRate 必须存在且在 (0, 1] 范围内。");
		var progressLossRates = new Dictionary<FishCatchDifficulty, float>();
		foreach (var tier in profilesDocument.ProgressLossTiers ?? Array.Empty<ProgressLossTierDefinition>())
		{
			if (tier == null || !Enum.IsDefined(tier.Difficulty) ||
				!float.IsFinite(tier.LossRate) || tier.LossRate <= 0 || tier.LossRate > 1)
			{
				validationErrors.Add("progressLossTiers 必须包含有效难度和 (0, 1] 的 lossRate。");
				continue;
			}
			if (!progressLossRates.TryAdd(tier.Difficulty, tier.LossRate))
				validationErrors.Add($"重复的进度掉落档位：{tier.Difficulty}");
		}
		foreach (var difficulty in Enum.GetValues<FishCatchDifficulty>())
			if (!progressLossRates.ContainsKey(difficulty))
				validationErrors.Add($"缺少进度掉落档位：{difficulty}");
		if (progressLossRates.Count == 3 &&
			!(progressLossRates[FishCatchDifficulty.Low] < progressLossRates[FishCatchDifficulty.Medium] &&
			  progressLossRates[FishCatchDifficulty.Medium] < progressLossRates[FishCatchDifficulty.High]))
			validationErrors.Add("进度掉落速度必须满足：简单 < 中等 < 困难。");

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
		ValidateDinnerRecipes(fishById.Values, validationErrors);
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
			eventsById,
			profilesDocument.SpecialVariantMultiplier,
			profilesDocument.ProgressCapacityMultiplier,
			progressLossRates,
			profilesDocument.ProgressGainRate
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

	private static void ValidateDinnerRecipes(
		IEnumerable<FishDefinition> fishDefinitions,
		List<string> errors)
	{
		Dictionary<string, int> favoriteCounts = new(StringComparer.Ordinal);
		foreach (FishDefinition fish in fishDefinitions)
		{
			DinnerRecipeDefinition recipe = fish.DinnerRecipe;
			if (recipe == null)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(recipe.DisplayName) ||
				string.IsNullOrWhiteSpace(recipe.Description) ||
				recipe.FavoriteCharacterIds == null || recipe.LikedCharacterIds == null)
			{
				errors.Add($"鱼种 {fish.Id} 的晚餐菜谱缺少名称、说明或偏好列表。");
				continue;
			}

			HashSet<string> recipients = new(StringComparer.Ordinal);
			foreach (string id in recipe.FavoriteCharacterIds)
			{
				if (!ValidateStableId(id, $"鱼种 {fish.Id} 的晚餐角色", errors))
				{
					continue;
				}

				if (!recipients.Add(id))
				{
					errors.Add($"鱼种 {fish.Id} 的晚餐偏好重复引用角色：{id}");
					continue;
				}

				favoriteCounts[id] = favoriteCounts.GetValueOrDefault(id) + 1;
				if (favoriteCounts[id] > 2)
				{
					errors.Add($"角色 {id} 的最爱料理超过两种。");
				}
			}

			foreach (string id in recipe.LikedCharacterIds)
			{
				if (ValidateStableId(id, $"鱼种 {fish.Id} 的晚餐角色", errors) &&
					!recipients.Add(id))
				{
					errors.Add($"鱼种 {fish.Id} 的晚餐偏好重复引用角色：{id}");
				}
			}
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
			if (!profile.LossDifficulty.HasValue || !Enum.IsDefined(profile.LossDifficulty.Value))
				errors.Add($"Fight profile {profile.Id}: lossDifficulty must be Low, Medium or High.");

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

			if (string.IsNullOrWhiteSpace(definition.FightProfileId) ||
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
