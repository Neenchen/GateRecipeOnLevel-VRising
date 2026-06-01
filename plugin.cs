using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using LevelRecipeGate.Config;
using LevelRecipeGate.Patches;
using LevelRecipeGate.Services;

#if !NoVCF
using VampireCommandFramework;
#endif

namespace LevelRecipeGate
{
	[BepInPlugin(Id, Name, Version)]
	public sealed class Plugin : BasePlugin
	{
		public const string Id = "com.yourname.LevelRecipeGate";
		public const string Name = "LevelRecipeGate";
		public const string Version = "1.0.0";

		internal static Harmony Harmony;
		internal static ManualLogSource Logger;

public override void Load()
{
	Logger = Log;

	ConfigStore.EnsureConfigsExist();
	ConfigStore.LoadLevelRecipeBlocksFromDisk();

	PlayerLevelService.Load();   
	Harmony = new Harmony(Id);
	Harmony.PatchAll(typeof(CraftingPatch));

#if !NoVCF
	try
	{
		CommandRegistry.RegisterAll();
	}
	catch (Exception ex)
	{
		Logger.LogError($"[{Name}] Failed to register VCF commands: {ex}");
	}
#endif

	Logger.LogInfo($"[{Name}] {Version} loaded.");
	Logger.LogInfo($"[{Name}] Level-gated recipes: {ConfigStore.RecipeMinLevelByGuid.Count} | Enabled={ConfigStore.LevelRecipeBlocksEnabled}");
	Logger.LogInfo($"[{Name}] Config: {Path.GetFullPath(ConfigStore.LevelRecipeCfgFile)}");
}

		public override bool Unload()
		{
			try
			{
				Harmony?.UnpatchSelf();
			}
			catch (Exception ex)
			{
				Logger?.LogError(ex);
			}

			return true;
		}
	}
}
