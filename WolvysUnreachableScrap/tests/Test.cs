// Wolvy's Unreachable Scrap tests. Only this mod's DLL and the game's own DLLs are loaded.
// 1. Plugin.Clean: game object names to piece names.   2. Blocks.Rebuild/Describe: the JaggedBlocks setting.
// 3. Plugin.On: the master switch.   4. Loot.DropCentre: leaves drops alone unless it should move them.
// 5. The game hooks: Attack.AddHitPoint, the Attack fields read, MineRock5.DamageArea and its single drop spot
//    (the loot transpiler run on the game's real code), Room.Theme.SunkenCrypt.
using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Reflection;
using BepInEx.Configuration; using BepInEx.Logging; using HarmonyLib; using UnityEngine;
class T {
 static int fails = 0, passes = 0;
 static void Check(string name, bool ok, string got = "") {
  System.Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (got.Length > 0 ? " -> " + got : ""));
  if (ok) passes++; else fails++;
 }
 static Assembly mod; static Type plugin, blocks, loot;
 static object Call(Type t, string m, params object[] a) =>
  t.GetMethod(m, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, a);
 static void Set(Type t, string f, object v) =>
  t.GetField(f, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(null, v);
 static object Get(Type t, string f) =>
  t.GetField(f, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);

 static void Main() {
  mod = Assembly.LoadFrom("WolvysUnreachableScrap.dll");
  plugin = mod.GetType("WolvysUnreachableScrap.Plugin", true);
  blocks = mod.GetType("WolvysUnreachableScrap.Blocks", true);
  loot = mod.GetType("WolvysUnreachableScrap.Loot", true);
  Set(plugin, "Log", new ManualLogSource("tests"));
  // ConfigFile needs BepInEx's paths: point them at a temporary folder (nothing is written there)
  string tmp = Path.Combine(Path.GetTempPath(), "wus-tests-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(tmp);
  typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
   .Invoke(null, new object[] { Path.Combine(tmp, "valheim.exe"), tmp, tmp, new string[0] });
  var cfg = new ConfigFile(Path.Combine(Path.GetTempPath(), "wus-tests-" + Guid.NewGuid().ToString("N") + ".cfg"), false);

  // --- identity ---
  Check("plugin id is wolvy.valheim.unreachablescrap", (string)Get(plugin, "Guid") == "wolvy.valheim.unreachablescrap");
  Check("plugin name is Wolvy's Unreachable Scrap", (string)Get(plugin, "PluginName") == "Wolvy's Unreachable Scrap");
  string def = (string)Get(plugin, "DefaultJagged");
  Check("default jagged list has the 1x2 wall", def.Contains("SunkenKit_int_wall_1x2/stone_low"), def);
  Check("default jagged list has the 2x4 wall", def.Contains("SunkenKit_int_wall_2x4/stone"), def);
  Check("scrap pile prefab is mudpile2_frac", (string)Get(plugin, "PileName") == "mudpile2_frac");
  Check("log lines start with [WolvysUnreachableScrap]", (string)Get(plugin, "LogTag") == "[WolvysUnreachableScrap] ");

  // --- Plugin.Clean ---
  Func<string, string> C = x => (string)Call(plugin, "Clean", x);
  Check("Clean: numbered copy", C("SunkenKit_int_wall_1x2 (3)") == "SunkenKit_int_wall_1x2", C("SunkenKit_int_wall_1x2 (3)"));
  Check("Clean: (Clone)", C("SunkenKit_int_wall_2x4(Clone)") == "SunkenKit_int_wall_2x4", C("SunkenKit_int_wall_2x4(Clone)"));
  Check("Clean: (Clone) and spaces", C("  SunkenKit_int_floor_4x4(Clone)  ") == "SunkenKit_int_floor_4x4");
  Check("Clean: plain name unchanged", C("stone_low") == "stone_low");
  Check("Clean: null is empty", C(null) == "");
  Check("Clean: a name that is only brackets stays", C("(3)") == "(3)", C("(3)"));

  // --- Blocks.Rebuild / Describe (the JaggedBlocks setting) ---
  var jagged = cfg.Bind("General", "JaggedBlocks", def, "");
  Set(plugin, "JaggedBlocks", jagged);
  Set(plugin, "LogDetails", cfg.Bind("General", "LogDetails", false, ""));
  Func<string> D = () => { Call(blocks, "Rebuild"); return (string)Call(blocks, "Describe"); };
  var pairs = D().Split(new[] { ", " }, StringSplitOptions.None);
  Check("default list: 3 blocks (two walls + floor)", pairs.Length == 3, string.Join(" | ", pairs));
  Check("the floor always counts", pairs.Contains("SunkenKit_int_floor_4x4/stone"));
  jagged.Value = "";
  Check("empty list: only the floor", D() == "SunkenKit_int_floor_4x4/stone", D());
  jagged.Value = "  A / b ,C/d,bad,/x,y/, ,A/b";
  pairs = D().Split(new[] { ", " }, StringSplitOptions.None);
  Check("spaces trimmed, bad entries skipped, duplicates once", pairs.Length == 3 && pairs.Contains("A/b") && pairs.Contains("C/d"), string.Join(" | ", pairs));
  jagged.Value = def;
  Check("back to the default list", D().Split(new[] { ", " }, StringSplitOptions.None).Length == 3);

  // --- Plugin.On (master switch) ---
  var enabled = cfg.Bind("General", "Enabled", true, "");
  var reach = cfg.Bind("General", "ReachStuckScrap", true, "");
  Set(plugin, "Enabled", enabled);
  Func<ConfigEntry<bool>, bool> On = e => (bool)Call(plugin, "On", e);
  Check("On: enabled and on", On(reach));
  reach.Value = false; Check("On: setting off", !On(reach)); reach.Value = true;
  enabled.Value = false; Check("On: mod disabled turns every part off", !On(reach)); enabled.Value = true;
  Check("On: null setting is off", !On((ConfigEntry<bool>)null));

  // --- Loot.DropCentre leaves drops where the game puts them unless it should move them ---
  var lootAtFeet = cfg.Bind("General", "LootAtFeet", true, "");
  Set(plugin, "LootAtFeet", lootAtFeet);
  var centre = new Vector3(1f, 3002f, 3f);
  Func<Vector3> Drop = () => (Vector3)Call(loot, "DropCentre", centre, null, null);
  Check("DropCentre: no hit info, drop unchanged", Drop() == centre);
  lootAtFeet.Value = false; Check("DropCentre: LootAtFeet off, drop unchanged", Drop() == centre); lootAtFeet.Value = true;
  enabled.Value = false; Check("DropCentre: mod disabled, drop unchanged", Drop() == centre); enabled.Value = true;

  // --- the game code the mod hooks (this game version) ---
  var add = AccessTools.Method(typeof(Attack), "AddHitPoint");
  var ps = add?.GetParameters();
  Check("Attack.AddHitPoint exists", add != null);
  Check("AddHitPoint(list, GameObject, Collider, Vector3, float, bool)", ps != null && ps.Length == 6 && ps[1].ParameterType == typeof(GameObject)
        && ps[2].ParameterType == typeof(Collider) && ps[3].ParameterType == typeof(Vector3) && ps[5].ParameterType == typeof(bool),
        ps == null ? "" : string.Join(", ", ps.Select(p => p.ParameterType.Name).ToArray()));
  foreach (var f in new[] { "m_character", "m_weapon", "m_pickaxeSpecial" })
   Check("Attack." + f + " exists", AccessTools.Field(typeof(Attack), f) != null);
  var dmg = AccessTools.Method(typeof(MineRock5), "DamageArea");
  var dps = dmg?.GetParameters();
  Check("MineRock5.DamageArea(int, HitData) exists", dps != null && dps.Length == 2 && dps[0].ParameterType == typeof(int) && dps[1].ParameterType == typeof(HitData));
  Check("Room.Theme.SunkenCrypt exists", Enum.IsDefined(typeof(Room.Theme), "SunkenCrypt"));
  Check("MineRock5.m_hitEffectAreaCenter exists", AccessTools.Field(typeof(MineRock5), "m_hitEffectAreaCenter") != null);
  if (dmg != null) {
   var original = PatchProcessor.GetOriginalInstructions(dmg);
   var tr = loot.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
   var result = ((IEnumerable<CodeInstruction>)tr.Invoke(null, new object[] { original })).ToList();
   bool patched = (bool)Get(loot, "_patched");
   Check("loot hook finds exactly one drop spot in the game's DamageArea", patched, (result.Count - original.Count) + " instructions added");
   int at = result.FindIndex(i => i.opcode == System.Reflection.Emit.OpCodes.Call && (i.operand as MethodInfo)?.Name == "DropCentre");
   Check("DropCentre call sits right before Random.insideUnitSphere", at > 0 && result[at + 1].opcode == System.Reflection.Emit.OpCodes.Call
         && (result[at + 1].operand as MethodInfo)?.Name == "get_insideUnitSphere");
  }
  System.Console.WriteLine(fails == 0 ? "ALL PASSED (" + passes + " checks)" : "FAILURES: " + fails + " of " + (passes + fails));
  Environment.Exit(fails);
 }
}
