using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StructureDamageTweaks
{
    [BepInPlugin(PluginId, "Structure Damage Tweaks", "1.1.0")]
    public class StructureDamageTweaks : BaseUnityPlugin
    {
        static public ConfigEntry<bool> preventTamedDamage;
        static public ConfigEntry<bool> preventTamedDamageNonPlayer;
        public class Category
        {
            public string id;
            public string name;
            public float baseMultiplier;
            public bool onlyPlayerStructures = true;
            public List<string> globalKeys;
            public List<float> globalKeysMultipliers;
            public List<string> items;
            //public List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            public List<float> itemsMultipliers;
            public List<float> itemsRange;
            public List<float> itemsAutoRepairPercentage;
            float maxRange;
            bool differentRanges;
            int numRepairItems;
            int numProtectionItems;
            public string defaultGlobalKeys = "";
            public string defaultGlobalKeysMultipliers = "";
            public string defaultItems = "";
            public string defaultItemModifiers = "";
            public string defaultIdentifiers = "";
            static public readonly CultureInfo cultureInfo = new CultureInfo("en-US");
            virtual public bool IsInCategory(WearNTear __instance)
            {
                if (onlyPlayerStructures && (__instance.GetComponent<Piece>() == null  || __instance.GetComponent<Piece>().GetCreator() == 0L))
                    return false;
                return true;
            }
            public float ComputeMultiplier(ref WearNTear __instance)
            {
                float ret = 1;
                for (int i = 0; i < globalKeys.Count; ++i)
                {
                    if (ZoneSystem.instance.GetGlobalKey(globalKeys[i]))
                    {
                        ret = globalKeysMultipliers[i];
                        Log("Modify damage by " + globalKeysMultipliers[i] + " due to global key " + (globalKeys[i]));
                        break;
                    }
                }
                if (numProtectionItems > 0)
                {
                    List<Player> players = GetPlayersInRadius(__instance.transform.position, maxRange);
                    float modifier = 1;
                    foreach (Player player in players)
                    {
                        for (int i = 0; i < items.Count; ++i)
                        {
                            if (itemsMultipliers[i] >= modifier || (differentRanges && Vector3.Distance(player.transform.position, __instance.transform.position) >= itemsRange[i]))
                                continue;
                            //if (player.GetInventory().ContainsItem(items[i]))
                            if (CheckItem(player, items[i]))
                            {
                                modifier = itemsMultipliers[i];
                                Log("Modify damage by " + itemsMultipliers[i] + " due to item " + (items[i].ToString()));
                                break;
                            }
                        }
                    }
                    ret *= modifier;
                    if (players.Count>1)
                        Log("Applying best item multiplier found: "+modifier);
                }
                if (ret != 1)
                    Log("Applied total damage factor of '" + ret + "' from category name '" + name + "' which has the id '" + id + "'.");
                return ret;
            }
            public float ComputeAutoRepair(WearNTear __instance)
            {
                float res = 0;
                if (numRepairItems > 0)
                {
                    List<Player> players = GetPlayersInRadius(__instance.transform.position, maxRange);
                    foreach (Player player in players)
                    {
                        for (int i = 0; i < items.Count; ++i)
                        {
                            if (itemsAutoRepairPercentage[i] <= res || (differentRanges && Vector3.Distance(player.transform.position, __instance.transform.position) >= itemsRange[i]))
                                continue;
                            //if (player.GetInventory().ContainsItem(items[i]))
                            if (CheckItem(player, items[i]))
                            {
                                res = itemsAutoRepairPercentage[i];
                                Log("Autorepair by " + itemsAutoRepairPercentage[i] + "% due to item " + (items[i].ToString()));
                                break;
                            }
                        }
                    }
                    if (players.Count > 1)
                        Log("Applying best item auptorepair found: " + res + "%");
                }
                return res;
            }
            virtual public void Init(StructureDamageTweaks parent)
            {
                name = parent.Config.Bind<string>(id, "name", name, "Only used in game with logging enabled. Though I advice naming your categories for clarity").Value;
                onlyPlayerStructures = parent.Config.Bind<bool>(id, "OnlyPlayerStructures", onlyPlayerStructures, "Determines whether this group only applies to structures build by any player. Be aware, that if this is false, this may include beehives or other structures that you actually want to destroy.").Value;
                string tmp = parent.Config.Bind<string>(id, "GlobalKeysNames", defaultGlobalKeys, "Global keys that apply modifiers. First applicable key will be used. Example values: defeated_eikthyr,defeated_gdking,defeated_bonemass,defeated_dragon,defeated_goblinking. EVA: Killed_HelDemon,Killed_Jotunn,Killed_SvartalfarQueen").Value;
                globalKeys = tmp.Split(',').ToList().ConvertAll(x => x.Trim(' '));
                globalKeysMultipliers = StringToFloatList(1f,parent.Config.Bind<string>(id, "GlobalKeysMultipliers", defaultGlobalKeysMultipliers, "Damage multipliers per name. First applicable multiplier will be used. This list should have the same length as 'GlobalKeysNames'. Values that cannot be converted to a float will be ignored. Values below zero would repair a structure and are not adviced. Below one reduces damage to structures. Above one will increase damage.").Value);
                StripList < string >(ref globalKeys, globalKeysMultipliers.Count);

                tmp = parent.Config.Bind<string>(id, "ItemInInventoryRequirements", "Yagluth thing", "'Token name' of items that apply modifiers if in player inventory. See https://github.com/Valheim-Modding/Wiki/wiki/ObjectDB-Table for vanilla items. You can use 'structuredamagetweaks inventory' ingame to print a full list of 'Token names' for what is currently in your inventory. First applicable item with an actuall effect (see below) in this list will be used.").Value;
                items = tmp.Split(',').ToList().ConvertAll(x => x.Trim(' '));
                itemsMultipliers = StringToFloatList(1f, parent.Config.Bind<string>(id, "ItemInInventoryMultipliers", "0.5", "Damage multipliers per item. First applicable multiplier that is unequal to '1' will be used. This list should have the same length as 'ItemInInventoryRequirements'. Missing or non-float-convertable ones will be set to '1', i.e. no effect. Values below zero would repair a structure and are not adviced. Below one reduces damage to structures. Above one will increase damage.").Value);
                numProtectionItems = itemsMultipliers.Count(x => x != 1.0);
                FillList<float>(ref itemsMultipliers, items.Count, 1f);
                itemsAutoRepairPercentage = StringToFloatList(0f,parent.Config.Bind<string>(id, "ItemInInventoryAutoRepairPercentage", "5", "Amount of auto-repair per item. All structures in range will be repaired by this percentage every '[AutoRepair]Timer' seconds. First applicable non-zero value will be used. This list should have the same length as 'ItemInInventoryRequirements', missing entries will be set to zero, i.e. no effect.").Value);
                numRepairItems = itemsAutoRepairPercentage.Count(x => x != 0);
                FillList<float>(ref itemsAutoRepairPercentage, items.Count, 0f);
                itemsRange = StringToFloatList(10f, parent.Config.Bind<string>(id, "ItemInInventoryRange", "100", "Range per item.Checked per structure.This is a restriction as to when an item is 'applicable', see above.This list should have the same length as 'ItemInInventoryRequirements', missing entries will be set to the maximum given range, or '10' should the whole list be empty.Different items with different ranges can work together if ordered correctly: Put strong short range items first.").Value);
                if (itemsRange.Count > 0)
                    maxRange = itemsRange.Max();
                else 
                    maxRange = 100;
                FillList<float>(ref itemsRange, items.Count, maxRange);
                differentRanges = maxRange != itemsRange.Min();
            }
        }
        public class NameCategory : Category
        {
            public List<string> structureNames;
            override public bool IsInCategory(WearNTear __instance)
            {
                if (!base.IsInCategory(__instance))
                    return false;
                return structureNames.IndexOf(__instance.gameObject.name.ToLower().Replace("(clone)", "")) != -1;
            }
            override public void Init(StructureDamageTweaks parent)
            {
                string tmp = parent.Config.Bind<string>(id, "StructureName", defaultIdentifiers, "Names of the structure(s) in this category.").Value;
                structureNames = tmp.Split(',').ToList().ConvertAll(x => x.ToLower().Replace("(clone)", "").Trim(' '));
                base.Init(parent);
            }
        }
        public class MaterialCategory : Category
        {
            public List<WearNTear.MaterialType> types = new List<WearNTear.MaterialType>();
            override public bool IsInCategory(WearNTear __instance)
            {
                if (!base.IsInCategory(__instance))
                    return false;
                return types.IndexOf(__instance.m_materialType) != -1;
            }
            override public void Init(StructureDamageTweaks parent)
            {
                string tmp = parent.Config.Bind<string>(id, "MaterialTypes", defaultIdentifiers, "Names of the material(s) in this category. Possibly values: Wood, Stone, Iron, HardWood").Value;
                var tmpList = tmp.Split(',').ToList().ConvertAll(x => x.ToLower());
                foreach (var s in tmpList)
                {
                    switch (s)
                    {
                        case "wood":
                            types.Add(WearNTear.MaterialType.Wood);
                            break;
                        case "stone":
                            types.Add(WearNTear.MaterialType.Stone);
                            break;
                        case "iron":
                            types.Add(WearNTear.MaterialType.Iron);
                            break;
                        case "hardwood":
                            types.Add(WearNTear.MaterialType.HardWood);
                            break;
                        default:
                            break;
                    }
                }
                base.Init(parent);
            }
        }

        public const string PluginId = "StructureDamageTweaks";
        private static StructureDamageTweaks _instance;
        private static ConfigEntry<bool> _loggingEnabled;

        public static List<Category> categories;
        public uint autoRepairTimer;
        private Coroutine autoRepairRoutine;


        private Harmony _harmony;

        public static void Log(string message)
        {
            if (_loggingEnabled.Value)
                _instance.Logger.LogInfo(message);
        }

        public static void LogWarning(string message)
        {
            if (_loggingEnabled.Value)
                _instance.Logger.LogWarning(message);
        }

        public static void LogError(string message)
        {
            //if (_loggingEnabled.Value)
            _instance.Logger.LogError(message);
        }

        private void Awake()
        {
            _instance = this;
            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginId);
            categories = new List<Category>();
            Init();
        }

        public void Init()
        {
            _loggingEnabled = Config.Bind("Logging", "Logging Enabled", false, "Enable logging. Please be aware, that enabling this together with auto repair will slow your game each time auto repair is performed if you are in a region with many instances that are in need of repairs.");
            preventTamedDamage = Config.Bind("TamedCreatures", "PreventTamedDamage", true, "If enabled, tamed creatures do no damage to player created structures.");
            preventTamedDamageNonPlayer = Config.Bind("TamedCreatures", "NonPlayerStructures", false, "If enabled, extends PreventTamedDamage to non-player-structures.");
            uint catNum = Config.Bind<uint>("Category", "NumberOfCategories", 1, "Number of additional categories for which damage reductions may be defined. Remark: You can use a category to exclude structures from another category. Each structure can always be in only one category and they are tested zero first, ..., default last").Value;
            autoRepairTimer = Config.Bind<uint>("AutoRepair", "Timer", 120, "Timer in seconds on how often auto repair is applied if the player has an according item (see in categories). Disabled if value is zero. Low values not adviced.").Value;
            for (uint i = 0; i < catNum; ++i)
            {
                string id = "Category" + i;
                string type = Config.Bind<string>(id, "type", "structureName", "What is used to determine if a structure in inside this category. Options: 'structureName', 'material', 'none'. In '[CategoryDefault]' this is always 'none'.").Value;
                if (type.ToLower() == "structurename")
                    categories.Add(new NameCategory() { id = id });
                else if (type.ToLower() == "material")
                    categories.Add(new MaterialCategory() { id = id });
                else if (type.ToLower() == "none")
                    categories.Add(new Category() { id = id });
                else
                    LogError("Invalid type");
            }
            if (categories.Count > 0)
            {
                categories[0].name = "Ships";
                categories[0].defaultIdentifiers = "raft,karve,vikingship,littleboat,cargoship,bigcargoship";
                categories[0].defaultGlobalKeys = "Killed_HelDemon,Killed_Jotunn,Killed_SvartalfarQueen,defeated_goblinking";
                categories[0].defaultGlobalKeysMultipliers = "0.01,0.2,0.3,0.5";
            }
            //categories.Add(new MaterialCategory() { id = "Category" + catNum });
            //{
            //    categories.Last().name = "IronExample";
            //    categories.Last().defaultIdentifiers = "Iron";
            //    categories.Last().defaultGlobalKeys = "Killed_HelDemon,Killed_Jotunn,Killed_SvartalfarQueen,defeated_goblinking";
            //    categories.Last().defaultGlobalKeysMultipliers = "0.01,0.2,0.3,0.5";
            //}
            categories.Add(new Category() { id = "CategoryDefault", name = "Default", defaultGlobalKeys = "Killed_HelDemon,Killed_Jotunn,Killed_SvartalfarQueen,defeated_goblinking", defaultGlobalKeysMultipliers = "0.1,0.2,0.3,0.5" });
            foreach (var cat in categories)
            {
                cat.Init(this);

            }
            if (autoRepairTimer!=0)
                autoRepairRoutine = StartCoroutine(DelayedRepair());
        }

        private void OnDestroy()
        {
            categories.Clear();
            categories = null;
            _instance = null;
            _harmony?.UnpatchSelf();
        }

        public void Reload()
        {
            Config.Reload();
            categories.Clear();
            StopCoroutine(autoRepairRoutine);
            Init();
        }

        public static List<Player> GetPlayersInRadius(Vector3 point, float range)
        {
            List<Player> players = new List<Player>();
            foreach (Player player in Player.GetAllPlayers())
            {
                if (Vector3.Distance(player.transform.position, point) < range)
                {
                    players.Add(player);
                }
            }

            return players;
        }

        public static bool CheckItem(Player player, string itemString)
        {
            var items = player.GetInventory()?.GetAllItems() ?? new List<ItemDrop.ItemData>(0);
            foreach (var item in items)
            {
                if (item.m_shared.m_name == itemString)
                    return true;
            }
            return false;
        }

        public static List<float> StringToFloatList(float defaultVal, string inString)
        {
            List<float> ret = new List<float>();
            List<string> tmpList = inString.Split(',').ToList();
            foreach (var s in tmpList)
            {
                if (float.TryParse(s, NumberStyles.Any, Category.cultureInfo, out float f))
                    ret.Add(f);
                else
                    ret.Add(defaultVal);
            }
            return ret;
        }

        public static void StripList<T>(ref List<T> l, int len)
        {
            if (l.Count > len)
                l.RemoveRange(len, l.Count - len);
        }

        public static void FillList<T>(ref List<T> l, int len, T filler)
        {
            if (l.Count < len)
                l.InsertRange(l.Count, Enumerable.Repeat(filler, len - l.Count));
        }

        //internal static ItemDrop GetItemWithPrefab(string prefabName)
        //{
        //    ItemDrop result;
        //    try
        //    {
        //        result = ObjectDB.instance.GetItemPrefab(prefabName).GetComponent<ItemDrop>();
        //    }
        //    catch
        //    {
        //        LogError("Error grabbing the prefab name from the ObjectDB: " + prefabName);
        //        result = null;
        //    }
        //    return result;
        //}

        private static object GetInstanceField<T>(T instance, string fieldName)
		{
			FieldInfo field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return field.GetValue(instance);
			}
			return null;
		}

        private IEnumerator DelayedRepair()
        {
            for (; ; )
            {
                if (ZNetScene.instance)
                {
                    List<WearNTear> allInstaces = WearNTear.GetAllInstances();
                    if (allInstaces.Count > 0)
                    {
                        foreach (WearNTear wearNTear in allInstaces)
                        {

                            if (!wearNTear || wearNTear.GetComponent<Piece>() == null)
                                continue;
                            ZNetView znetView = (ZNetView)GetInstanceField<WearNTear>(wearNTear, "m_nview");
                            if (!(znetView == null))
                            {
                                float num = znetView.GetZDO().GetFloat("health", 0f);
                                if (num != 0f && num != wearNTear.m_health)
                                {
                                    foreach (var cat in categories)
                                    {
                                        if (cat.IsInCategory(wearNTear))
                                        {
                                            float repair = cat.ComputeAutoRepair(wearNTear);
                                            if (num != 0f)
                                            {
                                                num += wearNTear.m_health / 100f * repair;
                                                if (num > wearNTear.m_health)
                                                    num = wearNTear.m_health;
                                                znetView.GetZDO().Set("health", num);
                                            }
                                            break;
                                        }
                                    }
                                }
                            }

                                    
                            
                            //{
                            //    float @float = znetView.GetZDO().GetFloat("health", 0f);
                            //    if ((double)@float > 0.0 && (double)@float < (double)wearNTear.m_health)
                            //    {
                            //        float num = @float + (float)((double)wearNTear.m_health * (double)BetterWardsPlugin.AutoRepairAmount / 100.0);
                            //        if ((double)num > (double)wearNTear.m_health)
                            //        {
                            //            num = wearNTear.m_health;
                            //        }
                            //        znetView.GetZDO().Set("health", num);
                            //        znetView.InvokeRPC(ZNetView.Everybody, "WNTHealthChanged", new object[]
                            //        {
                            //            num
                            //        });
                            //    }
                            //}
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(autoRepairTimer);
            }
            //yield break;
        }

        [HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
        public static class StructureDamage
        {
            private static bool Prefix(ref WearNTear __instance, ref float damage)
            {
                Log("Pre change structure damage: "+damage);
                foreach( var cat in categories)
                {
                    if (cat.IsInCategory(__instance))
                    {
                        damage *= cat.ComputeMultiplier(ref __instance);
                        break;
                    }
                }
                Log("Post change structure damage: " + damage);
                return true;
            }

        }

        [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
        private static class TamedStructureDamage
        {
            private static void Prefix(ref HitData hit, Piece ___m_piece)
            {
                if (StructureDamageTweaks.preventTamedDamage.Value && hit!=null)
                {
                    Character attacker= hit.GetAttacker();
                    if (attacker!=null)
                    {
                        if (attacker.IsTamed())
                        {
                            if (StructureDamageTweaks.preventTamedDamageNonPlayer.Value || (___m_piece != null && ___m_piece.GetCreator() != 0L))
                            {
                                hit.m_damage.Modify(0);
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Terminal), "InputText")]
        private static class InputText_Patch
        {
            // Token: 0x06000005 RID: 5 RVA: 0x000021B0 File Offset: 0x000003B0
            private static bool Prefix(Terminal __instance)
            {
                string text = __instance.m_input.text.ToLower();
                if (text.StartsWith("structuredamagetweaks"))
                {
                    if (text.ToLower().Equals("structuredamagetweaks reset"))
                    {
                        StructureDamageTweaks._instance.Reload();
                        Traverse.Create(__instance).Method("AddString", new object[]
                        {
                        text
                        }).GetValue();
                        Traverse.Create(__instance).Method("AddString", new object[]
                        {
                        "Structure Damage Tweaks config reloaded"
                        }).GetValue();
                    }
                    else if (text.ToLower().Equals("structuredamagetweaks inventory"))
                    {
                        foreach (var item in Player.m_localPlayer.GetInventory()?.GetAllItems() ?? new List<ItemDrop.ItemData>(0))
                        {
                            string s = "Item Token Name: '" + item.m_shared.m_name + "'";
                            _instance.Logger.LogInfo(s); //this also outputs to console
                            //Log("item.m_shared.m_itemType: " + item.m_shared.m_itemType);
                            //if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Misc)
                            //    item.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                        }
                        Traverse.Create(__instance).Method("AddString", new object[]
                        {
                        "Finished printing inventory"
                        }).GetValue();
                    }
                    else
                    {
                        Traverse.Create(__instance).Method("AddString", new object[]
                        {
                        "Incorrect argument. Possible: with keyword 'reset': Reload config of Stucture DamageTweaks. With keyword 'inventory': Display item names currently in your inventory (for use in config file)"
                        }).GetValue();
                    }
                    return false;
                }
                return true;
            }
        }
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        public static class TerminalInitConsole_Patch
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("structuredamagetweaks", "with keyword 'reset': Reload config of Stucture DamageTweaks. With keyword 'inventory': Display item names currently in your inventory (for use in config file)", null);
            }
        }
    }
    
}
