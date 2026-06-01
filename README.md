<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
# GateRecipeOnLevel
The mod tracks the highest gear level a player has ever reached.
=======
# GateLevelRecipe
>>>>>>> 049dc7d (Initial commit)
=======
=======
>>>>>>> b7d93f5 (Update README.md)
# GateLevelRecipe
=======
# LevelRecipeGate

Clean V Rising server mod made from RecipeTweaker, simplified to one job:

**Block crafting recipes until the player reaches the configured gear level.**

Removed from this clean version:
- Drop blocking
- Servant loot blocking
- Scheduled unlocks
- Global `blocked_recipes.txt`

## Config

Generated at:

```txt
BepInEx/config/LevelRecipeGate/level_recipe_blocks.json
```

Example:

```json
{
  "enabled": true,
  "message": "You cannot craft this yet. Required gear level: {level}.",
  "recipe_level_blocks": [
    {
      "min_level": 33,
      "recipes": [
        305819079,
        -1520452495
      ]
    },
    {
      "min_level": 50,
      "recipes": [
        690858507
      ]
    }
  ]
}
```

Supported placeholders in `message`:
- `{level}` = required gear level
- `{current}` = detected current player level, or `unknown`
- `{recipe}` = recipe GUID

## Commands

```txt
.levelrecipegate reload
.levelrecipegate list
.levelrecipegate add <level> <recipeGuid>
.levelrecipegate remove <recipeGuid>
```

Examples:

```txt
.levelrecipegate add 74 -1671420432
.levelrecipegate remove -1671420432
.levelrecipegate list
.levelrecipegate reload
```

## Build

From the project folder:

```powershell
dotnet build -c Release -p:VRisingServerDir="G:\SteamLibrary\steamapps\common\VRising\VRising_Server"
```

Output:

```txt
bin\Release\net6.0\LevelRecipeGate.dll
```

Copy to:

```txt
VRising_Server\BepInEx\plugins\
```

## Important

If a player still can craft a blocked recipe, check `BepInEx/LogOutput.log` for:

```txt
[LevelRecipeGate] Craft check recipe=..., playerLevel=..., requiredLevel=...
```

That line tells us if:
- the recipe GUID matched,
- the player level was detected,
- and the required level was found.
>>>>>>> cb39b31 (Please enter the commit message for your changes. Lines starting)
>>>>>>> a9b3f57 (Please enter the commit message for your changes. Lines starting)
