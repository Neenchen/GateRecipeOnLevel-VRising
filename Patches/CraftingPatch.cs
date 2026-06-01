using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using LevelRecipeGate.Config;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using LevelRecipeGate.Services;

namespace LevelRecipeGate.Patches
{
	[HarmonyPatch(typeof(StartCraftingSystem), "OnUpdate")]
	public static class CraftingPatch
	{
		[HarmonyPrefix]
		public static void Prefix(StartCraftingSystem __instance)
		{
			EntityManager entityManager = __instance.EntityManager;
			EntityQuery entityQuery;

			try
			{
				FieldInfo field = typeof(StartCraftingSystem).GetField("_StartCraftItemEventQuery", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				entityQuery = (((field != null ? field.GetValue(__instance) : null) as EntityQuery?) ?? entityManager.CreateEntityQuery(new EntityQueryDesc[]
				{
					new EntityQueryDesc
					{
						All = new ComponentType[]
						{
							ComponentType.ReadOnly<FromCharacter>(),
							ComponentType.ReadOnly<StartCraftItemEvent>()
						},
						Options = (EntityQueryOptions)2
					}
				}));
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"[{Plugin.Name}] Failed creating craft query: {ex.Message}");
				return;
			}

			NativeArray<Entity> entities = default(NativeArray<Entity>);

			try
			{
				entities = entityQuery.ToEntityArray(Allocator.Temp);

				for (int i = 0; i < entities.Length; i++)
				{
					Entity eventEntity = entities[i];

					if (!entityManager.HasComponent<StartCraftItemEvent>(eventEntity))
						continue;

					StartCraftItemEvent craftEvent = entityManager.GetComponentData<StartCraftItemEvent>(eventEntity);

					if (!TryGetRecipeGuid(craftEvent, out PrefabGUID recipeGuid))
						continue;

					if (!ConfigStore.TryGetRequiredLevel(recipeGuid.GuidHash, out int requiredLevel))
						continue;

					int playerGearLevel = TryGetCraftingPlayerGearLevel(entityManager, eventEntity, out User user);

					Plugin.Logger.LogInfo($"[{Plugin.Name}] Craft check recipe={recipeGuid.GuidHash}, playerGearLevel={playerGearLevel}, requiredLevel={requiredLevel}");

					if (playerGearLevel < 0)
					{
						entityManager.DestroyEntity(eventEntity);

						TrySendSystemMessage(
   						 entityManager,
   						 user,
    						"Unable to determine your gear level. Crafting blocked.");

						Plugin.Logger.LogWarning($"[{Plugin.Name}] BLOCKED recipe={recipeGuid.GuidHash}: could not read player gear level.");
					}
					else if (playerGearLevel < requiredLevel)
					{
						entityManager.DestroyEntity(eventEntity);

						string message = ConfigStore.LevelRecipeBlockedMessage
							.Replace("{level}", requiredLevel.ToString())
							.Replace("{current}", playerGearLevel.ToString())
							.Replace("{recipe}", recipeGuid.GuidHash.ToString());

						TrySendSystemMessage(entityManager, user, message);

						Plugin.Logger.LogInfo($"[{Plugin.Name}] BLOCKED recipe={recipeGuid.GuidHash}, playerGearLevel={playerGearLevel}, requiredLevel={requiredLevel}");
					}
					else
					{
						Plugin.Logger.LogInfo($"[{Plugin.Name}] ALLOWED recipe={recipeGuid.GuidHash}, playerGearLevel={playerGearLevel}, requiredLevel={requiredLevel}");
					}
				}
			}
			finally
			{
				if (entities.IsCreated)
					entities.Dispose();
			}
		}



		private static Entity TryGetLocalCharacterFromUser(EntityManager entityManager, Entity userEntity)
		{
			if (userEntity == Entity.Null || !entityManager.Exists(userEntity))
				return Entity.Null;

			try
			{
				if (!entityManager.HasComponent<User>(userEntity))
					return Entity.Null;

				User user = entityManager.GetComponentData<User>(userEntity);
				object boxedUser = user;

				string[] preferredNames =
				{
					"LocalCharacter",
					"Character",
					"ControlledCharacter",
					"CurrentCharacter",
					"PlayerCharacter",
					"_LocalCharacter",
					"_Character"
				};

				foreach (string name in preferredNames)
				{
					if (TryGetEntityMember(boxedUser, name, out Entity found) && found != Entity.Null && entityManager.Exists(found))
					{
						Plugin.Logger.LogInfo($"[{Plugin.Name}] User character source: User.{name} => {found.Index}:{found.Version}");
						return found;
					}
				}

				Type userType = boxedUser.GetType();

				foreach (FieldInfo field in userType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					object value = field.GetValue(boxedUser);
					if (TryConvertToEntity(value, out Entity found) && found != Entity.Null && entityManager.Exists(found))
					{
						Plugin.Logger.LogInfo($"[{Plugin.Name}] User character source scan field: {field.Name} => {found.Index}:{found.Version}");
						return found;
					}
				}

				foreach (PropertyInfo property in userType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (property.GetIndexParameters().Length != 0)
						continue;

					object value = property.GetValue(boxedUser);
					if (TryConvertToEntity(value, out Entity found) && found != Entity.Null && entityManager.Exists(found))
					{
						Plugin.Logger.LogInfo($"[{Plugin.Name}] User character source scan property: {property.Name} => {found.Index}:{found.Version}");
						return found;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"[{Plugin.Name}] Failed resolving User.LocalCharacter: {ex.Message}");
			}

			return Entity.Null;
		}

		private static bool TryGetEntityMember(object obj, string name, out Entity entity)
		{
			entity = Entity.Null;

			if (obj == null)
				return false;

			Type type = obj.GetType();

			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && TryConvertToEntity(field.GetValue(obj), out entity))
				return true;

			PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.GetIndexParameters().Length == 0 && TryConvertToEntity(property.GetValue(obj), out entity))
				return true;

			return false;
		}

		private static bool TryConvertToEntity(object value, out Entity entity)
		{
			entity = Entity.Null;

			if (value == null)
				return false;

			if (value is Entity directEntity)
			{
				entity = directEntity;
				return true;
			}

			Type type = value.GetType();

			string[] entityNames =
			{
				"Entity",
				"_Entity",
				"Value",
				"value",
				"Character",
				"LocalCharacter"
			};

			foreach (string name in entityNames)
			{
				FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.FieldType == typeof(Entity))
				{
					entity = (Entity)field.GetValue(value);
					return true;
				}

				PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null && property.PropertyType == typeof(Entity) && property.GetIndexParameters().Length == 0)
				{
					entity = (Entity)property.GetValue(value);
					return true;
				}
			}

			foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (field.FieldType == typeof(Entity))
				{
					entity = (Entity)field.GetValue(value);
					return true;
				}
			}

			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (property.PropertyType == typeof(Entity) && property.GetIndexParameters().Length == 0)
				{
					entity = (Entity)property.GetValue(value);
					return true;
				}
			}

			return false;
		}

		private static int TryGetCraftingPlayerGearLevel(EntityManager entityManager, Entity eventEntity, out User user)
{
	user = default;

	try
	{
		if (!entityManager.HasComponent<FromCharacter>(eventEntity))
			return -1;

		FromCharacter fromCharacter = entityManager.GetComponentData<FromCharacter>(eventEntity);

		if (fromCharacter.User == Entity.Null ||
		    !entityManager.Exists(fromCharacter.User) ||
		    !entityManager.HasComponent<User>(fromCharacter.User))
			return -1;

		user = entityManager.GetComponentData<User>(fromCharacter.User);

		int highestLevel = PlayerLevelService.UpdateAndGetHighestGearLevel(entityManager, user);

		Plugin.Logger.LogInfo(
			$"[{Plugin.Name}] Highest recorded gear level for {user.CharacterName} = {highestLevel}");

		return highestLevel;
	}
	catch (Exception ex)
	{
		Plugin.Logger.LogWarning(
			$"[{Plugin.Name}] Failed reading highest player gear level: {ex.Message}");

		return -1;
	}
}

		private static bool LooksLikeGearLevelComponent(string componentName)
		{
			if (string.IsNullOrWhiteSpace(componentName))
				return false;

			string n = componentName.ToLowerInvariant();

			if (n.Contains("weapon") || n.Contains("item") || n.Contains("power") || n.Contains("physical") || n.Contains("spell"))
				return false;

			return n.Contains("gear") ||
			       n.Contains("equipment") ||
			       n.Contains("playercharacter") ||
			       n.Contains("unitstats") ||
			       n.Contains("buffedunitstats");
		}

		private static int TryExtractGearLevelValue(object component, string componentName)
		{
			if (component == null)
				return -1;

			Type type = component.GetType();

			string[] exactNames =
			{
				"GearLevel",
				"TotalGearLevel",
				"EquipmentLevel",
				"TotalEquipmentLevel",
				"MaxGearLevel",
				"HighestGearLevel",
				"MaxEquipmentLevel",
				"HighestEquipmentLevel"
			};

			foreach (string name in exactNames)
			{
				if (TryReadIntMember(type, component, name, out int value) && IsValidGearLevel(value))
				{
					Plugin.Logger.LogInfo($"[{Plugin.Name}] Gear level member found: {componentName}.{name} = {value}");
					return value;
				}
			}

			foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				string n = field.Name.ToLowerInvariant();

				if (!IsAllowedGearLevelMemberName(n))
					continue;

				if (TryConvertToIntDeep(field.GetValue(component), out int value) && IsValidGearLevel(value))
				{
					Plugin.Logger.LogInfo($"[{Plugin.Name}] Gear level field scan found: {componentName}.{field.Name} = {value}");
					return value;
				}
			}

			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (property.GetIndexParameters().Length != 0)
					continue;

				string n = property.Name.ToLowerInvariant();

				if (!IsAllowedGearLevelMemberName(n))
					continue;

				if (TryConvertToIntDeep(property.GetValue(component), out int value) && IsValidGearLevel(value))
				{
					Plugin.Logger.LogInfo($"[{Plugin.Name}] Gear level property scan found: {componentName}.{property.Name} = {value}");
					return value;
				}
			}

			return -1;
		}

		private static bool IsAllowedGearLevelMemberName(string lowerName)
		{
			if (string.IsNullOrWhiteSpace(lowerName))
				return false;

			if (lowerName.Contains("weapon") ||
			    lowerName.Contains("item") ||
			    lowerName.Contains("physical") ||
			    lowerName.Contains("spell") ||
			    lowerName.Contains("power") ||
			    lowerName.Contains("damage") ||
			    lowerName.Contains("resistance"))
				return false;

			return lowerName.Contains("gear") ||
			       lowerName.Contains("equipment") ||
			       lowerName.Contains("armorgear") ||
			       lowerName.Contains("totalgear") ||
			       lowerName.Contains("highestgear") ||
			       lowerName.Contains("maxgear");
		}

		private static bool TryReadIntMember(Type type, object obj, string name, out int value)
		{
			value = -1;

			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && TryConvertToIntDeep(field.GetValue(obj), out value))
				return true;

			PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.GetIndexParameters().Length == 0 && TryConvertToIntDeep(property.GetValue(obj), out value))
				return true;

			return false;
		}

		private static bool IsValidGearLevel(int value)
		{
			return value >= 1 && value <= 200;
		}

		private static void DumpInterestingComponents(EntityManager entityManager, Entity entity, string label)
		{
			if (label != "User.LocalCharacter")
				return;

			NativeArray<ComponentType> dumpTypes = default(NativeArray<ComponentType>);
			try
			{
				dumpTypes = entityManager.GetComponentTypes(entity, Allocator.Temp);

				for (int i = 0; i < dumpTypes.Length; i++)
				{
					Type managedType = TryGetManagedType(dumpTypes[i]);

					string typeName = dumpTypes[i].ToString();
					if (managedType != null)
						typeName += " | managed=" + managedType.FullName;

					Plugin.Logger.LogInfo($"[{Plugin.Name}] DEBUG Character component: {typeName}");
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"[{Plugin.Name}] Component debug dump failed for {label}: {ex.Message}");
			}
			finally
			{
				if (dumpTypes.IsCreated)
					dumpTypes.Dispose();
			}
		}

		private static Type ResolveType(string fullName)
		{
			Type type = Type.GetType(fullName) ?? Type.GetType(fullName + ", ProjectM");
			if (type != null)
				return type;

			try
			{
				foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					type = assembly.GetType(fullName, false, false);
					if (type != null)
						return type;
				}
			}
			catch
			{
			}

			return null;
		}

		private static Type TryGetManagedType(ComponentType componentType)
		{
			try
			{
				MethodInfo method = typeof(ComponentType).GetMethod("GetManagedType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (method == null)
					return null;

				return method.Invoke(componentType, null) as Type;
			}
			catch
			{
				return null;
			}
		}

		private static object TryGetComponentDataBoxed(EntityManager entityManager, Entity entity, Type componentType)
		{
			try
			{
				MethodInfo hasComponent = typeof(EntityManager).GetMethods()
					.FirstOrDefault(m => m.Name == "HasComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

				MethodInfo getComponent = typeof(EntityManager).GetMethods()
					.FirstOrDefault(m => m.Name == "GetComponentData" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

				if (hasComponent == null || getComponent == null)
					return null;

				bool has = (bool)hasComponent.MakeGenericMethod(componentType).Invoke(entityManager, new object[] { entity });
				if (!has)
					return null;

				return getComponent.MakeGenericMethod(componentType).Invoke(entityManager, new object[] { entity });
			}
			catch
			{
				return null;
			}
		}

		private static bool TryConvertToIntDeep(object value, out int result)
		{
			if (TryConvertToInt(value, out result))
				return true;

			result = -1;

			if (value == null)
				return false;

			Type type = value.GetType();

			string[] nestedNames = { "Value", "_Value", "value", "m_Value", "RawValue", "BaseValue" };

			foreach (string name in nestedNames)
			{
				FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && TryConvertToInt(field.GetValue(value), out result))
					return true;

				PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null && property.GetIndexParameters().Length == 0 && TryConvertToInt(property.GetValue(value), out result))
					return true;
			}

			return false;
		}

		private static bool TryConvertToInt(object value, out int result)
		{
			result = -1;

			if (value == null)
				return false;

			try
			{
				if (value is int i) { result = i; return true; }
				if (value is short s) { result = s; return true; }
				if (value is byte b) { result = b; return true; }
				if (value is long l) { result = (int)l; return true; }
				if (value is float f) { result = (int)Math.Floor(f); return true; }
				if (value is double d) { result = (int)Math.Floor(d); return true; }

				return int.TryParse(value.ToString(), out result);
			}
			catch
			{
				return false;
			}
		}

		private static bool TryGetRecipeGuid(StartCraftItemEvent ev, out PrefabGUID guid)
		{
			Type eventType = typeof(StartCraftItemEvent);
			object boxed = ev;

			string[] preferredNames =
			{
				"RecipePrefab",
				"Recipe",
				"RecipeGuid",
				"RecipePrefabGuid",
				"m_Recipe",
				"_Recipe"
			};

			foreach (string name in preferredNames)
			{
				FieldInfo field = eventType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.FieldType == typeof(PrefabGUID))
				{
					guid = (PrefabGUID)field.GetValue(boxed);
					return true;
				}
			}

			foreach (FieldInfo field in eventType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (field.FieldType == typeof(PrefabGUID))
				{
					guid = (PrefabGUID)field.GetValue(boxed);
					return true;
				}
			}

			guid = default(PrefabGUID);
			return false;
		}

private static void TrySendSystemMessage(EntityManager entityManager, User user, string message)
{
	try
	{
		FixedString512Bytes fixedMessage = message;

		Type chatUtils =
			ResolveType("ProjectM.Network.ServerChatUtils") ??
			ResolveType("ProjectM.ServerChatUtils") ??
			ResolveType("ProjectM.Network.ChatMessageServerSystem") ??
			ResolveType("ProjectM.ChatMessageServerSystem");

		if (chatUtils == null)
		{
			Plugin.Logger.LogWarning($"[{Plugin.Name}] Chat message failed: no chat utility type found.");
			return;
		}

		MethodInfo method = chatUtils
			.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			.FirstOrDefault(m => m.Name == "SendSystemMessageToClient");

		if (method == null)
		{
			Plugin.Logger.LogWarning(
				$"[{Plugin.Name}] Chat message failed: SendSystemMessageToClient method not found on {chatUtils.FullName}.");
			return;
		}

		ParameterInfo[] parameters = method.GetParameters();

		if (parameters.Length == 3)
		{
			method.Invoke(null, new object[]
			{
				entityManager,
				user,
				fixedMessage
			});

			Plugin.Logger.LogInfo(
				$"[{Plugin.Name}] Sent blocked recipe chat message to {user.CharacterName}.");
			return;
		}

		if (parameters.Length == 2)
		{
			method.Invoke(null, new object[]
			{
				user,
				fixedMessage
			});

			Plugin.Logger.LogInfo(
				$"[{Plugin.Name}] Sent blocked recipe chat message to {user.CharacterName}.");
			return;
		}

		Plugin.Logger.LogWarning(
			$"[{Plugin.Name}] Chat message failed: unsupported SendSystemMessageToClient signature with {parameters.Length} parameter(s).");
	}
	catch (Exception ex)
	{
		Plugin.Logger.LogWarning(
			$"[{Plugin.Name}] Could not send system message: {ex}");
	}
}
	}
}
