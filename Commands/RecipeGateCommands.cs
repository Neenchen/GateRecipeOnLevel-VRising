using System;
using System.Linq;
using LevelRecipeGate.Config;

#if !NoVCF
using VampireCommandFramework;
#endif

namespace LevelRecipeGate.Commands
{
#if !NoVCF
	[CommandGroup("levelrecipegate", null)]
	public static class RecipeGateCommands
	{
		[Command("reload", null, null, "Reload level_recipe_blocks.json", null, true)]
		public static void Reload(ICommandContext ctx)
		{
			ConfigStore.LoadLevelRecipeBlocksFromDisk();
			ctx.Reply($"[LevelRecipeGate] Reloaded {ConfigStore.RecipeMinLevelByGuid.Count} level-gated recipe(s). Enabled={ConfigStore.LevelRecipeBlocksEnabled}.");
		}

		[Command("list", null, null, "List level-gated recipe GUIDs", null, true)]
		public static void List(ICommandContext ctx)
		{
			if (ConfigStore.RecipeMinLevelByGuid.Count == 0)
			{
				ctx.Reply("[LevelRecipeGate] No level-gated recipes configured.");
				return;
			}

			foreach (var group in ConfigStore.RecipeMinLevelByGuid.GroupBy(kvp => kvp.Value).OrderBy(g => g.Key))
			{
				int[] values = group.Select(kvp => kvp.Key).OrderBy(x => x).ToArray();
				ctx.Reply($"[LevelRecipeGate] Level {group.Key}: {values.Length} recipe(s).");

				for (int i = 0; i < values.Length; i += 10)
					ctx.Reply(string.Join(", ", values.Skip(i).Take(10)));
			}
		}

		[Command("add", null, null, "Add or update a level gate: .levelrecipegate add <level> <recipeGuid>", null, true)]
		public static void Add(ICommandContext ctx, int minLevel, int recipeGuid)
		{
			if (minLevel < 0 || minLevel > 255)
			{
				ctx.Reply("[LevelRecipeGate] Invalid level. Use 0-255.");
				return;
			}

			ConfigStore.SetRecipeLevelGate(recipeGuid, minLevel);
			ctx.Reply($"[LevelRecipeGate] Recipe {recipeGuid} now requires gear level {minLevel}.");
		}

		[Command("remove", null, null, "Remove a recipe level gate: .levelrecipegate remove <recipeGuid>", null, true)]
		public static void Remove(ICommandContext ctx, int recipeGuid)
		{
			if (ConfigStore.RemoveRecipeLevelGate(recipeGuid))
				ctx.Reply($"[LevelRecipeGate] Removed level gate for recipe {recipeGuid}.");
			else
				ctx.Reply($"[LevelRecipeGate] Recipe {recipeGuid} was not level-gated.");
		}
	}
#else
	internal static class RecipeGateCommands { }
#endif
}
