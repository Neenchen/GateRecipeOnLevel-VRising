using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LevelRecipeGate.Config
{
	internal static class ConfigStore
	{
		private static string ConfigDir => Path.Combine("BepInEx", "config", "LevelRecipeGate");
		public static string LevelRecipeCfgFile => Path.Combine(ConfigDir, "level_recipe_blocks.json");

		internal static readonly Dictionary<int, int> RecipeMinLevelByGuid = new Dictionary<int, int>();
		internal static bool LevelRecipeBlocksEnabled = true;
		internal static string LevelRecipeBlockedMessage = "You cannot craft this yet. Required gear level: {level}.";

		private sealed class LevelRecipeConfigFile
		{
			public bool enabled { get; set; } = true;
			public string message { get; set; } = "You cannot craft this yet. Required gear level: {level}.";
			public List<LevelRecipeBlockEntry> recipe_level_blocks { get; set; } = new List<LevelRecipeBlockEntry>();
		}

		private sealed class LevelRecipeBlockEntry
		{
			public int min_level { get; set; }
			public List<int> recipes { get; set; } = new List<int>();
		}

		internal static void EnsureConfigsExist()
		{
			Directory.CreateDirectory(ConfigDir);

			if (!File.Exists(LevelRecipeCfgFile))
			{
				var example = new LevelRecipeConfigFile
				{
					enabled = true,
					message = "You cannot craft this yet. Required gear level: {level}.",
					recipe_level_blocks = new List<LevelRecipeBlockEntry>
					{
						new LevelRecipeBlockEntry
						{
							min_level = 33,
							recipes = new List<int> { 305819079, -1520452495 }
						},
						new LevelRecipeBlockEntry
						{
							min_level = 50,
							recipes = new List<int> { 690858507, -1690827442 }
						}
					}
				};

				File.WriteAllText(LevelRecipeCfgFile, JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true }));
			}
		}

		internal static void LoadLevelRecipeBlocksFromDisk()
		{
			RecipeMinLevelByGuid.Clear();
			LevelRecipeBlocksEnabled = true;
			LevelRecipeBlockedMessage = "You cannot craft this yet. Required gear level: {level}.";

			try
			{
				EnsureConfigsExist();

				string json = File.ReadAllText(LevelRecipeCfgFile);
				var cfg = JsonSerializer.Deserialize<LevelRecipeConfigFile>(
					json,
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				if (cfg == null)
				{
					Plugin.Logger.LogWarning($"[{Plugin.Name}] Level recipe config was empty.");
					return;
				}

				LevelRecipeBlocksEnabled = cfg.enabled;
				if (!string.IsNullOrWhiteSpace(cfg.message))
					LevelRecipeBlockedMessage = cfg.message;

				if (cfg.recipe_level_blocks == null)
					return;

				foreach (var block in cfg.recipe_level_blocks)
				{
					if (block == null || block.recipes == null)
						continue;

					foreach (int guid in block.recipes)
					{
						// If the same recipe appears twice, keep the highest level requirement.
						if (!RecipeMinLevelByGuid.TryGetValue(guid, out int existing) || block.min_level > existing)
							RecipeMinLevelByGuid[guid] = block.min_level;
					}
				}

				Plugin.Logger.LogInfo($"[{Plugin.Name}] Loaded {RecipeMinLevelByGuid.Count} level-gated recipe GUID(s). Enabled={LevelRecipeBlocksEnabled}.");
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"[{Plugin.Name}] Failed loading level recipe config: {ex}");
			}
		}

		internal static void SaveLevelRecipeBlocksToDisk()
		{
			try
			{
				Directory.CreateDirectory(ConfigDir);

				var grouped = RecipeMinLevelByGuid
					.GroupBy(kvp => kvp.Value)
					.OrderBy(g => g.Key)
					.Select(g => new LevelRecipeBlockEntry
					{
						min_level = g.Key,
						recipes = g.Select(kvp => kvp.Key).OrderBy(x => x).ToList()
					})
					.ToList();

				var cfg = new LevelRecipeConfigFile
				{
					enabled = LevelRecipeBlocksEnabled,
					message = LevelRecipeBlockedMessage,
					recipe_level_blocks = grouped
				};

				File.WriteAllText(LevelRecipeCfgFile, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"[{Plugin.Name}] Failed saving level recipe config: {ex}");
			}
		}

		internal static bool TryGetRequiredLevel(int recipeGuid, out int requiredLevel)
		{
			requiredLevel = 0;
			return LevelRecipeBlocksEnabled && RecipeMinLevelByGuid.TryGetValue(recipeGuid, out requiredLevel);
		}

		internal static void SetRecipeLevelGate(int recipeGuid, int minLevel)
		{
			RecipeMinLevelByGuid[recipeGuid] = minLevel;
			SaveLevelRecipeBlocksToDisk();
		}

		internal static bool RemoveRecipeLevelGate(int recipeGuid)
		{
			bool removed = RecipeMinLevelByGuid.Remove(recipeGuid);
			if (removed)
				SaveLevelRecipeBlocksToDisk();
			return removed;
		}
	}
}
