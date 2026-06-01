using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace LevelRecipeGate.Services
{
	internal static class PlayerLevelService
	{
		private static string ConfigDir => Path.Combine("BepInEx", "config", "LevelRecipeGate");
		private static string FilePath => Path.Combine(ConfigDir, "player_highest_levels.json");

		private static readonly Dictionary<ulong, PlayerLevelRecord> Records = new();

		private sealed class PlayerLevelRecord
		{
			public string name { get; set; } = "";
			public ulong steam_id { get; set; }
			public int highest_gear_level { get; set; }
		}

		internal static void Load()
		{
			Directory.CreateDirectory(ConfigDir);

			if (!File.Exists(FilePath))
			{
				File.WriteAllText(FilePath, "{}");
				return;
			}

			string json = File.ReadAllText(FilePath);
			var loaded = JsonSerializer.Deserialize<Dictionary<ulong, PlayerLevelRecord>>(json);

			Records.Clear();

			if (loaded == null)
				return;

			foreach (var pair in loaded)
				Records[pair.Key] = pair.Value;
		}

		internal static void Save()
		{
			Directory.CreateDirectory(ConfigDir);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(Records, new JsonSerializerOptions { WriteIndented = true }));
		}

		internal static int UpdateAndGetHighestGearLevel(EntityManager em, User user)
		{
			int current = GetCurrentGearScore(em, user);

			ulong steamId = user.PlatformId;
			string name = user.CharacterName.ToString();

			if (!Records.TryGetValue(steamId, out PlayerLevelRecord record))
			{
				record = new PlayerLevelRecord
				{
					name = name,
					steam_id = steamId,
					highest_gear_level = current
				};

				Records[steamId] = record;
				Save();

				return record.highest_gear_level;
			}

			record.name = name;

			if (current > record.highest_gear_level)
			{
				record.highest_gear_level = current;
				Save();
			}

			return record.highest_gear_level;
		}

		private static int GetCurrentGearScore(EntityManager em, User user)
		{
			try
			{
				Entity characterEntity = user.LocalCharacter._Entity;

				if (characterEntity == Entity.Null || !em.Exists(characterEntity))
					return 0;

				if (!em.HasComponent<Equipment>(characterEntity))
					return 0;

				Equipment equipment = em.GetComponentData<Equipment>(characterEntity);

				float gearScore = equipment.ArmorLevel + equipment.SpellLevel + equipment.WeaponLevel;
				return (int)Math.Round(gearScore, MidpointRounding.AwayFromZero);
			}
			catch
			{
				return 0;
			}
		}
	}
}