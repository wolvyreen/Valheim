// Wolvy's Unreachable Scrap - lets the pickaxe reach muddy scrap stuck in the Sunken Crypts' jagged blocks and floor,
// drops that scrap's loot at your feet when it would fall out of the world, and marks stuck scrap with a small glint so you can find it.
// Author: Wolvyreen(With_ClaudeAI)
//
// The problem (read from assembly_valheim.dll and tested in game, 2026-11-08):
//   Attack.DoMeleeAttack casts rays across the swing; each ray's hits are sorted by distance and the first object
//   goes to Attack.AddHitPoint, then the ray stops. In Sunken Crypts some scrap pile bits (prefab mudpile2_frac,
//   a MineRock5) sit against or inside crypt blocks (the 1x2 and 2x4 wall pieces, and the floor). The swing starts at
//   the player's body, so it lands on the block first, even while the crosshair (a ray from the camera) is on the
//   scrap. And MineRock5.DamageArea drops a bit's loot at the bit's centre (mudpile2_frac has
//   m_hitEffectAreaCenter = true), which for a stuck bit is inside the rock, so the loot can fall out of the dungeon.
//
// Fix 1 (reach): a prefix on Attack.AddHitPoint. ONLY when ALL of these hold: the swing landed on a jagged block
//   (setting JaggedBlocks, plus the crypt floor SunkenKit_int_floor_4x4/stone, always) in a Sunken Crypt room, the
//   local player is swinging a pickaxe inside a dungeon, and a mudpile2_frac bit is under the crosshair (only scrap you
//   can see counts, unless ReachHiddenScrap is on): the hit is handed to that bit instead, with the same arguments
//   vanilla would pass for a direct hit on it. The game then damages it the normal way (MineRock5.Damage -> RPC to the
//   pile's owner), so damage, tool tier and loot are vanilla and it works on servers without the mod.
// Fix 2 (loot): a transpiler on MineRock5.DamageArea wraps the drop centre (the value read just before
//   Random.insideUnitSphere). ONLY for mudpile2_frac hit by a player inside a dungeon, and only when the line from
//   the player's chest to that centre is blocked by a crypt room's own geometry: the centre moves to the player's feet.
// Indicator (Markers): while a pickaxe is held in a dungeon, crypt scrap bits within Distance are checked once a
//   second. A bit counts as stuck when it touches a jagged block or the floor and is mostly inside crypt rock, or is a
//   low sliver a swing passes over. Stuck bits you can see get a small glint (or a soft glow), drawn after the crypt's
//   water and pulled toward the camera so the scrap itself doesn't hide it.
//
// Cost: the prefix runs for every ray of every melee attack, so its first test is a cached per-collider answer;
// everything else only runs on a jagged block. Pile checks are cached per pile and the crosshair ray is cast once per swing.
//
// Settings: the shared Wolvyreen Mod Hub window (src/WolvyreenModHub.cs + src/ModHubUi/, shared verbatim by all of
// Wolvyreen's mods; never edit them in one mod only) and Configuration Manager both show them.
//
// Build note: compiled with Mono mcs, which mishandles HarmonyX FieldRefAccess (ref-return delegates), so the field
// getters are made with DynamicMethod instead.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WolvysUnreachableScrap
{
    [BepInPlugin(Guid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wolvy.valheim.unreachablescrap";
        public const string PluginName = "Wolvy's Unreachable Scrap";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Wolvyreen(With_ClaudeAI)";
        internal const string LogTag = "[WolvysUnreachableScrap] ";   // every log line starts with it (build rule 10)

        internal const string PileName = "mudpile2_frac";
        // found in game: 1x2 on 2026-11-08 at two stuck piles; 2x4 on 2026-11-09 (dozens of swings at stuck scrap landed on it)
        internal const string DefaultJagged = "SunkenKit_int_wall_1x2/stone_low, SunkenKit_int_wall_2x4/stone";
        internal const string FloorBlock = "SunkenKit_int_floor_4x4/stone";       // the crypt floor next to stuck piles
        internal const float CrosshairReach = 4.0f;  // metres past the chest for the crosshair bit
        internal const float DungeonHeight = 3000f;  // dungeons are built above this height (Character.InInterior)

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ReachStuckScrap;
        internal static ConfigEntry<bool> ReachHiddenScrap;
        internal static ConfigEntry<bool> LootAtFeet;
        internal static ConfigEntry<string> JaggedBlocks;
        internal static ConfigEntry<bool> LogDetails;
        internal static ConfigEntry<bool> HighlightStuckScrap;
        internal static ConfigEntry<bool> DebugText;
        internal static ConfigEntry<HighlightStyle> Style;
        internal static ConfigEntry<Color> HighlightColour;
        internal static ConfigEntry<float> HighlightDistance;
        internal static ConfigEntry<float> HighlightBrightness;
        internal static ConfigEntry<float> HighlightSpeed;

        /// How stuck scrap is made to stand out.
        internal enum HighlightStyle { Glint, Glow }

        internal static int SolidAndPileMask;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Turns the whole mod on or off. Off, every swing and every drop is exactly as in the normal game.");
            ReachStuckScrap = Config.Bind("General", "ReachStuckScrap", true,
                "Lets your pickaxe hit muddy scrap stuck in the jagged crypt blocks. " +
                "Only the scrap under your crosshair (where the Muddy Scrap Pile name shows), when your swing lands on a jagged block in front of it; scrap hidden behind rock stays out of reach.");
            ReachHiddenScrap = Config.Bind("General", "ReachHiddenScrap", false,
                "Also reaches stuck scrap you can't see. " +
                "Off: only exposed scrap counts (the bit under your crosshair, and only those are highlighted). On: a swing on a jagged block can also go to the hidden bit closest to where you aim, within 1 m of where the swing lands, and hidden bits you can get close to are highlighted too; that is scrap the normal game keeps out of reach.");
            LootAtFeet = Config.Bind("General", "LootAtFeet", true,
                "Drops loot from a buried crypt scrap bit at your feet instead of inside the rock. " +
                "Only for Sunken Crypt scrap pile bits whose loot would appear behind the crypt's rock; every other drop lands as normal.");
            JaggedBlocks = Config.Bind("General", "JaggedBlocks", DefaultJagged,
                "The crypt blocks that trap scrap, as piece/part, separated by commas. " +
                "Only these blocks are treated as jagged. With LogDetails on, the log names any other crypt block your swing lands on next to scrap, so you can add it here.");
            DebugText = Config.Bind("General", "DebugText", false,
                "Shows on screen what the stuck scrap indicator is doing, for finding problems. " +
                "A panel below the hotbar lists each marked scrap bit and why the bit under your crosshair is or isn't marked; on screen, " +
                "a cyan number marks each bit, yellow + its visible spots and a red * the glint while it is drawn.");
            LogDetails = Config.Bind("General", "LogDetails", false,
                "Writes each reach and each moved drop to the log. " +
                "It also names crypt blocks next to scrap that are not in JaggedBlocks, so a block that still traps scrap can be added.");

            HighlightStuckScrap = Config.Bind("Scrap Indicator", "ShowIndicator", true,
                "Shows where stuck crypt scrap is, so you can find it. " +
                "Only while you hold a pickaxe and are close by, and only for stuck bits you can mine (with ReachHiddenScrap off: bits with some part showing); scrap in the open is left as it is.");
            Style = Config.Bind("Scrap Indicator", "Style", HighlightStyle.Glint,
                "How the indicator looks. " +
                "Glint: now and then a small sparkle flashes on the part of the scrap you can see. Glow: a soft, slowly pulsing glow over the scrap.");
            HighlightColour = Config.Bind("Scrap Indicator", "Colour", new Color(1f, 0.62f, 0.2f, 1f),
                "The colour of the glints or glow on stuck scrap.");
            HighlightDistance = Config.Bind("Scrap Indicator", "Distance", 8f,
                new ConfigDescription("How close you need to be for the indicator to show, in metres. It fades in over the last 2 m.",
                                      new AcceptableValueRange<float>(3f, 15f)));
            HighlightBrightness = Config.Bind("Scrap Indicator", "Brightness", 1f,
                new ConfigDescription("How bright the glints or glow are. 1 is the normal strength; lower is subtler, higher stands out more.",
                                      new AcceptableValueRange<float>(0.2f, 3f)));
            HighlightSpeed = Config.Bind("Scrap Indicator", "Speed", 1f,
                new ConfigDescription("How fast the glints come and go, or how fast the glow pulses. 1 is the normal speed; 2 is twice as fast, 0.5 half as fast.",
                                      new AcceptableValueRange<float>(0.25f, 4f)));
            HighlightStuckScrap.SettingChanged += (s, e) => Markers.Clear();
            Style.SettingChanged += (s, e) => Markers.Clear();

            SolidAndPileMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "terrain", "piece");
            Blocks.Rebuild();
            JaggedBlocks.SettingChanged += (s, e) => Blocks.Rebuild();
            LogDetails.SettingChanged += (s, e) => Blocks.Rebuild();
            ReachHiddenScrap.SettingChanged += (s, e) => Markers.Forget();

            _harmony = new Harmony(Guid + "." + System.Guid.NewGuid().ToString("N").Substring(0, 8));

            try { WolvyreenModHub.Hub.Register(Guid, PluginName, PluginVersion, Config, Logger, _harmony); }
            catch (Exception e) { Log.LogWarning(Plugin.LogTag + "Mod hub: could not add " + PluginName + ": " + e.Message); }

            bool reachOk = Reach.Patch(_harmony);
            bool lootOk = Loot.Patch(_harmony);
            Log.LogInfo(Plugin.LogTag + PluginName + " " + PluginVersion + " loaded (reach " + (reachOk ? "patched" : "NOT patched") +
                        ", loot " + (lootOk ? "patched" : "NOT patched") + "; jagged blocks: " + Blocks.Describe() + ").");
        }

        private void Update() => Markers.Tick();

        private void OnGUI()
        {
            if (DebugText != null && DebugText.Value) Markers.DebugGui();
        }

        private void OnDestroy()
        {
            Markers.Clear();
            try { WolvyreenModHub.Hub.Unregister(); } catch (Exception e) { Log?.LogWarning(Plugin.LogTag + "Mod hub: " + e.Message); }
            _harmony?.UnpatchSelf();
        }

        internal static bool On(ConfigEntry<bool> e) => Enabled != null && Enabled.Value && e != null && e.Value;

        internal static string Clean(string name)
        {
            if (name == null) return "";
            int i = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (i >= 0) name = name.Substring(0, i);
            name = name.Trim();
            // "SunkenKit_int_wall_1x2 (3)" -> "SunkenKit_int_wall_1x2"
            if (name.EndsWith(")", StringComparison.Ordinal))
            {
                int open = name.LastIndexOf(" (", StringComparison.Ordinal);
                if (open > 0) name = name.Substring(0, open);
            }
            return name;
        }

        internal static string ParentName(Collider c) => c != null && c.transform.parent != null ? Clean(c.transform.parent.name) : "?";

        internal static bool InCryptRoom(Collider c)
        {
            var room = c.GetComponentInParent<Room>();
            return room != null && room.m_theme == Room.Theme.SunkenCrypt;
        }

        internal const float NearbyReach = 1.0f;     // ReachHiddenScrap: metres from where the swing landed

        internal static Vector3 Chest(Component who) => who.transform.position + Vector3.up * 1.2f;
    }

    /// Which colliders are jagged blocks. Answers are cached per collider; the cache is cleared when the list changes.
    internal static class Blocks
    {
        internal enum Kind : byte { None, CryptOther, Jagged }

        private static HashSet<string> _pairs = new HashSet<string>(StringComparer.Ordinal);  // "piece/part"
        private static HashSet<string> _parts = new HashSet<string>(StringComparer.Ordinal);  // "part" only, the quick test
        private static readonly Dictionary<int, Kind> Cache = new Dictionary<int, Kind>();
        private const int CacheLimit = 8192;

        internal static void Rebuild()
        {
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in (Plugin.JaggedBlocks.Value ?? "").Split(','))
            {
                string p = raw.Trim();
                int slash = p.IndexOf('/');
                if (slash <= 0 || slash == p.Length - 1) continue;
                pairs.Add(p.Substring(0, slash).Trim() + "/" + p.Substring(slash + 1).Trim());
            }
            pairs.Add(Plugin.FloorBlock);   // the crypt floor always counts (Wolvyreen, 2026-11-09: permanent, no setting)
            var parts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string p in pairs) parts.Add(p.Substring(p.IndexOf('/') + 1));
            bool changed = !pairs.SetEquals(_pairs);
            _pairs = pairs;
            _parts = parts;
            Cache.Clear();                 // also needed when only LogDetails changed
            if (changed) Markers.Forget();  // re-check stuck bits only when the block list really changed
        }

        internal static string Describe() => _pairs.Count == 0 ? "none" : string.Join(", ", new List<string>(_pairs).ToArray());

        internal static Kind Of(Collider c)
        {
            int id = c.GetInstanceID();
            Kind k;
            if (Cache.TryGetValue(id, out k)) return k;
            k = Work(c);
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[id] = k;
            return k;
        }

        private static Kind Work(Collider c)
        {
            // Cheapest first: only colliders whose part name is in the list can be jagged. With LogDetails on, every
            // crypt block is looked at too (to name unknown ones), so the cache is cleared when LogDetails changes.
            bool partMatch = _parts.Contains(c.name);
            if (!partMatch && !Plugin.LogDetails.Value) return Kind.None;
            if (c.transform.parent == null || !Plugin.InCryptRoom(c)) return Kind.None;
            if (c.GetComponentInParent<IDestructible>() != null) return Kind.None;
            if (partMatch && _pairs.Contains(Plugin.ParentName(c) + "/" + c.name)) return Kind.Jagged;
            return Kind.CryptOther;
        }
    }

    /// Crypt scrap piles, cached per pile (only once the pile's network data is there).
    internal static class Piles
    {
        private static readonly Dictionary<int, bool> Cache = new Dictionary<int, bool>();
        private const int CacheLimit = 4096;

        internal static bool IsCryptPile(MineRock5 rock)
        {
            if (rock == null) return false;
            int id = rock.GetInstanceID();
            bool yes;
            if (Cache.TryGetValue(id, out yes)) return yes;
            var nv = rock.GetComponent<ZNetView>() ?? rock.GetComponentInParent<ZNetView>();
            var zdo = nv != null ? nv.GetZDO() : null;
            if (zdo == null || ZNetScene.instance == null) return false; // not ready: don't remember a "no"
            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            yes = prefab != null && prefab.name == Plugin.PileName;
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[id] = yes;
            return yes;
        }

        internal static Collider BitOf(Collider c) => c != null && IsCryptPile(c.GetComponentInParent<MineRock5>()) ? c : null;
    }

    /// Fix 1: hand a swing that landed on a jagged crypt block to the stuck scrap bit.
    internal static class Reach
    {
        private static Func<Attack, object> _character, _weapon, _pickaxeSpecial;
        private static readonly Collider[] Near = new Collider[64];
        private static bool _failed;

        // the crosshair bit, worked out once per swing
        private static Attack _swing;
        private static int _swingFrame = -1;
        private static Collider _swingBit;
        private static Ray _aim;            // the camera ray of this swing
        private static bool _haveAim;

        internal static bool Patch(Harmony h)
        {
            var target = AccessTools.Method(typeof(Attack), "AddHitPoint");
            _character = Getter("m_character");
            _weapon = Getter("m_weapon");
            _pickaxeSpecial = Getter("m_pickaxeSpecial");
            var ps = target?.GetParameters();
            if (target == null || _character == null || _weapon == null || _pickaxeSpecial == null || ps.Length != 6
                || ps[1].ParameterType != typeof(GameObject) || ps[2].ParameterType != typeof(Collider)
                || ps[3].ParameterType != typeof(Vector3) || ps[5].ParameterType != typeof(bool))
            {
                Plugin.Log.LogError(Plugin.LogTag + "Attack.AddHitPoint not found or changed in this game version; reaching stuck scrap is off.");
                return false;
            }
            h.Patch(target, prefix: new HarmonyMethod(typeof(Reach), nameof(Prefix)));
            return true;
        }

        private static Attack _diagSwing;
        private static int _diagFrame = -1;
        private static readonly Collider[] DiagNear = new Collider[256];

        /// LogDetails only: one line per pickaxe swing in a dungeon, about the first thing it touched and how far
        /// the nearest crypt scrap bit was from there (up to 3 m), so a bit that can't be reached can be explained.
        private static void Diagnose(Attack swing, Collider collider, Vector3 point)
        {
            if (ReferenceEquals(swing, _diagSwing) && Time.frameCount == _diagFrame) return;
            var who = _character(swing) as Component;
            if (who == null || !ReferenceEquals(who, Player.m_localPlayer) || who.transform.position.y < Plugin.DungeonHeight) return;
            var weapon = _weapon(swing) as ItemDrop.ItemData;
            if (weapon == null || weapon.m_shared == null || weapon.m_shared.m_skillType != Skills.SkillType.Pickaxes) return;
            _diagSwing = swing; _diagFrame = Time.frameCount;

            float nearest = float.MaxValue; string bitName = "";
            int n = Physics.OverlapSphereNonAlloc(point, 3f, DiagNear, Plugin.SolidAndPileMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Piles.BitOf(DiagNear[i]);
                if (c == null) continue;
                float d = Vector3.Distance(point, ClosestPoint(c, point));
                if (d < nearest) { nearest = d; bitName = c.name; }
            }
            Plugin.Log.LogInfo(Plugin.LogTag + "Swing touched " + Plugin.ParentName(collider) + "/" + collider.name
                               + " (" + Blocks.Of(collider) + ", " + Vector3.Distance(Plugin.Chest(who), point).ToString("0.00") + " m from chest)"
                               + (nearest < float.MaxValue ? "; nearest crypt scrap bit " + bitName + " at " + nearest.ToString("0.00") + " m" : "; no crypt scrap within 3 m"));
        }

        /// A compiled reader for one Attack field (FieldInfo.GetValue is slow; mcs breaks FieldRefAccess).
        private static Func<Attack, object> Getter(string name)
        {
            var f = AccessTools.Field(typeof(Attack), name);
            if (f == null) return null;
            var dm = new DynamicMethod("WolvysUnreachableScrap_get_" + name, typeof(object), new[] { typeof(Attack) }, typeof(Attack), true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f);
            if (f.FieldType.IsValueType) il.Emit(OpCodes.Box, f.FieldType);
            il.Emit(OpCodes.Ret);
            return (Func<Attack, object>)dm.CreateDelegate(typeof(Func<Attack, object>));
        }

        // Always lets the original run; only changes the arguments in the one case described at the top.
        private static void Prefix(Attack __instance, ref GameObject go, ref Collider collider, ref Vector3 point, ref bool multiCollider)
        {
            if (collider == null || !Plugin.On(Plugin.ReachStuckScrap)) return;
            try
            {
                if (Plugin.LogDetails.Value) Diagnose(__instance, collider, point);
                var kind = Blocks.Of(collider);               // cached; None for nearly every hit in the game
                if (kind == Blocks.Kind.None) return;

                var who = _character(__instance) as Component;
                if (who == null || !ReferenceEquals(who, Player.m_localPlayer)) return;
                var weapon = _weapon(__instance) as ItemDrop.ItemData;
                if (weapon == null || weapon.m_shared == null || weapon.m_shared.m_skillType != Skills.SkillType.Pickaxes) return;
                if (who.transform.position.y < Plugin.DungeonHeight) return;

                // only scrap you can see and point at, unless ReachHiddenScrap is on (Wolvyreen, 2026-11-08)
                Collider bit = CrosshairBit(__instance, who) ?? (Plugin.ReachHiddenScrap.Value ? AimedBit(point) : null);
                if (bit == null) return;
                if (kind != Blocks.Kind.Jagged)
                {
                    // A crypt block not in the list (only looked at with LogDetails on): left as normal, but named.
                    Plugin.Log.LogInfo(Plugin.LogTag + "Swing landed on " + Plugin.ParentName(collider) + "/" + collider.name
                                       + " next to a crypt scrap bit. That block is not in JaggedBlocks, so the swing was left as normal.");
                    return;
                }

                var rock = bit.GetComponentInParent<MineRock5>();
                Vector3 to = ClosestPoint(bit, point);
                if (Plugin.LogDetails.Value)
                    Plugin.Log.LogInfo(Plugin.LogTag + "Swing on " + Plugin.ParentName(collider) + "/" + collider.name
                                       + " handed to scrap bit " + bit.name + " (" + Vector3.Distance(point, to).ToString("0.00") + " m away).");
                Markers.Flash(bit);                                      // show that the hit went to the buried bit
                go = rock.gameObject;                                    // what Projectile.FindHitObject returns for the pile
                collider = bit;
                point = to;
                multiCollider = (bool)_pickaxeSpecial(__instance);       // = vanilla's value for a MineRock5
            }
            catch (Exception e)
            {
                if (!_failed) { _failed = true; Plugin.Log.LogWarning(Plugin.LogTag + "Reach check failed, swings stay as normal: " + e); }
            }
        }

        /// The crypt scrap bit under the crosshair, if the first solid thing the camera ray meets within reach is one.
        /// Cast once per swing (all of a swing's rays are handled in the same frame by the same Attack).
        private static Collider CrosshairBit(Attack swing, Component who)
        {
            if (ReferenceEquals(swing, _swing) && Time.frameCount == _swingFrame) return _swingBit;
            _swing = swing; _swingFrame = Time.frameCount; _swingBit = null; _haveAim = false;

            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return null;
            _aim = new Ray(cam.position, cam.forward); _haveAim = true;
            float range = Vector3.Distance(cam.position, Plugin.Chest(who)) + Plugin.CrosshairReach;
            RaycastHit hit;
            if (Physics.Raycast(cam.position, cam.forward, out hit, range, Plugin.SolidAndPileMask, QueryTriggerInteraction.Ignore))
                _swingBit = Piles.BitOf(hit.collider);
            return _swingBit;
        }

        /// ReachHiddenScrap only: among the crypt scrap bits within 1 m of where the swing landed, the one closest to
        /// where you are aiming (the crosshair line). No aim line: the one nearest the swing.
        private static Collider AimedBit(Vector3 point)
        {
            int n = Physics.OverlapSphereNonAlloc(point, Plugin.NearbyReach, Near, Plugin.SolidAndPileMask, QueryTriggerInteraction.Ignore);
            Collider best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = Piles.BitOf(Near[i]);
                if (c == null) continue;
                if (Vector3.Distance(point, ClosestPoint(c, point)) > Plugin.NearbyReach) continue;
                Vector3 centre = c.bounds.center;
                float score = _haveAim ? Vector3.Cross(_aim.direction, centre - _aim.origin).magnitude : Vector3.Distance(point, centre);
                if (score < bestScore) { best = c; bestScore = score; }
            }
            return best;
        }

        private static Vector3 ClosestPoint(Collider c, Vector3 p) => Markers.ClosestOn(c, p);
    }

    /// Fix 2: loot of a buried crypt scrap bit lands at the player's feet.
    internal static class Loot
    {
        private static bool _patched, _failed;

        internal static bool Patch(Harmony h)
        {
            var target = AccessTools.Method(typeof(MineRock5), "DamageArea");
            var ps = target?.GetParameters();
            if (target == null || ps.Length != 2 || ps[0].ParameterType != typeof(int) || ps[1].ParameterType != typeof(HitData))
            {
                Plugin.Log.LogError(Plugin.LogTag + "MineRock5.DamageArea not found or changed in this game version; loot at your feet is off.");
                return false;
            }
            h.Patch(target, transpiler: new HarmonyMethod(typeof(Loot), nameof(Transpiler)));
            return _patched;
        }

        // ... ldloc <centre>  [+ ldarg.0, ldarg.2, call DropCentre]  call Random.get_insideUnitSphere ...
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            var list = new List<CodeInstruction>(original);
            var inside = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "insideUnitSphere");
            int found = 0;
            for (int i = 1; i < list.Count; i++)
            {
                if (!list[i].Calls(inside) || !list[i - 1].IsLdloc()) continue;
                list.InsertRange(i, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Loot), nameof(DropCentre))),
                });
                found++;
                i += 3;
            }
            _patched = found == 1;
            if (!_patched)
            {
                Plugin.Log.LogError(Plugin.LogTag + "MineRock5.DamageArea has changed in this game version (" + found + " drop spots found, expected 1); loot at your feet is off.");
                return original;
            }
            return list;
        }

        public static Vector3 DropCentre(Vector3 centre, MineRock5 rock, HitData hit)
        {
            if (!Plugin.On(Plugin.LootAtFeet)) return centre;
            try
            {
                if (hit == null || !Piles.IsCryptPile(rock)) return centre;
                var player = hit.GetAttacker() as Player;
                if (player == null || player.transform.position.y < Plugin.DungeonHeight) return centre;

                Vector3 from = Plugin.Chest(player);
                Vector3 dir = centre - from;
                float len = dir.magnitude;
                if (len < 0.01f) return centre;
                foreach (var h in Physics.RaycastAll(from, dir / len, len, Plugin.SolidAndPileMask, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null) continue;
                    if (h.collider.transform.IsChildOf(rock.transform) || h.collider.transform.IsChildOf(player.transform)) continue;
                    if (!Plugin.InCryptRoom(h.collider)) continue; // only the crypt's own walls count
                    if (Plugin.LogDetails.Value)
                        Plugin.Log.LogInfo(Plugin.LogTag + "Loot of a buried scrap bit moved to your feet (rock in the way: "
                                           + Plugin.ParentName(h.collider) + "/" + h.collider.name + ").");
                    return player.transform.position + Vector3.up * 0.5f;
                }
                return centre;
            }
            catch (Exception e)
            {
                if (!_failed) { _failed = true; Plugin.Log.LogWarning(Plugin.LogTag + "Loot check failed, drops stay as normal: " + e); }
                return centre;
            }
        }
    }

    /// Glow markers on crypt scrap bits that are stuck in a jagged block.
    internal static class Markers
    {
        private const float ScanBeyondRange = 3f; // look a little further than HighlightDistance, so a highlight is ready as you walk up
        private const float ScanEvery = 1f;      // seconds
        private const int MaxMarkers = 24;
        private const float Deep = 0.03f;            // metres of overlap that count as touching a jagged block
        private const float MostlyBuried = 0.5f;     // share of the bit inside crypt rock for it to count as stuck
        private static readonly Collider[] Rock = new Collider[64];

        /// Share (0..1) of the bit's sample points (its middle and up to 24 corners) that are inside the crypt's own
        /// geometry (any room collider that isn't scrap).
        private static float BuriedShare(Collider bit)
        {
            Bounds b = bit.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.05f, Rock, Quaternion.identity, _blockMask, QueryTriggerInteraction.Ignore);
            var rock = new List<Collider>();
            for (int i = 0; i < n; i++)
                if (Rock[i] != null && !IsPile(Rock[i]) && Plugin.InCryptRoom(Rock[i])) rock.Add(Rock[i]);
            if (rock.Count == 0) return 0f;
            var pts = SamplePoints(bit);
            int inside = 0;
            foreach (var p in pts)
                foreach (var c in rock)
                    if (Inside(c, p)) { inside++; break; }
            return pts.Count == 0 ? 0f : (float)inside / pts.Count;
        }

        /// Is a point inside a collider? Boxes and other convex shapes: ClosestPoint returns the point itself. Concave
        /// meshes have no inside test, so: the shape is both above and below the point (a ray down from 5 m above and
        /// a ray up from 5 m below both meet it), which holds inside a floor slab or a lump of rock.
        private static bool Inside(Collider c, Vector3 p)
        {
            var mesh = c as MeshCollider;
            if (mesh == null || mesh.convex) return (c.ClosestPoint(p) - p).sqrMagnitude < 1e-6f;
            RaycastHit h;
            return c.Raycast(new Ray(p + Vector3.up * 5f, Vector3.down), out h, 5f)
                && c.Raycast(new Ray(p + Vector3.down * 5f, Vector3.up), out h, 5f);
        }
        private const float LowestBelowFeet = 1.5f;  // a bit stuck in the floor you stand on, or below the rubble you stand on (0.9.7: was 0.6)
        private const float HighestAboveFeet = 2.5f; // about as high as a pickaxe swing reaches

        private static readonly Collider[] Found = new Collider[1024];
        private static readonly Collider[] Blocking = new Collider[64];
        private static readonly Dictionary<int, bool> Stuck = new Dictionary<int, bool>();     // per bit, cached
        private static readonly Dictionary<int, bool> Reachable = new Dictionary<int, bool>(); // per bit, cached
        private static readonly Collider[] Body = new Collider[32];
        private static int _groundMask = -1;
        private static readonly Dictionary<int, Marker> Shown = new Dictionary<int, Marker>(); // per bit
        private static float _nextScan;
        private static int _pileMask = -1, _blockMask = -1;

        private sealed class Marker
        {
            public Collider Bit;
            public GameObject Edges;    // the glint: a small sparkle that faces the camera
            public Material Mat;
            public float FlashUntil;
            public readonly List<Vector3> Points = new List<Vector3>();  // spots on the bit you can see (updated each scan)
            public float NextAt;        // when the next glint starts
            public float StartAt = -10f;
            public bool Big;            // this glint is a hit (bigger, brighter)
            public Vector3 At;          // where the current glint is
            public bool Logged;         // LogDetails: its first glint was written to the log
            public bool IsGlow;         // Style Glow: Edges is a soft skin over the bit instead of a sparkle
            public float Phase;         // Glow: where in its pulse this bit starts, so nearby bits don't pulse together
            public float LastK, LastSize;   // DebugText: the glint's last brightness and size
        }

        private const float GlowGrow = 1.02f;      // Glow: the skin is 2% larger than the bit, so it sits just outside it
        private const float GlowPulse = 1.6f;      // Glow: seconds per pulse

        private const float GlintTime = 0.8f;      // seconds a glint lasts
        private const float GlintSize = 0.24f;     // metres across at its peak
        private static Mesh _quad;
        private static Texture2D _star;

        private static Shader _glowShader;
        private static readonly Dictionary<int, Mesh> GlowMeshes = new Dictionary<int, Mesh>(); // per source mesh

        /// A copy of the bit's shape with white vertex colours: particle shaders multiply by the vertex colour, and the
        /// scrap's own mesh has none (which can read as black, i.e. no glow). Cached per source mesh.
        private static Mesh GlowMesh(Mesh source)
        {
            int key = source.GetInstanceID();
            Mesh m;
            if (GlowMeshes.TryGetValue(key, out m) && m != null) return m;
            m = UnityEngine.Object.Instantiate(source);
            m.name = "WolvysUnreachableScrap_GlowShape";
            var white = new Color32[m.vertexCount];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            m.colors32 = white;
            if (GlowMeshes.Count > 512) GlowMeshes.Clear();
            GlowMeshes[key] = m;
            return m;
        }
        private static bool _additive;   // the chosen shader adds light (no darkening); else it blends see-through
        private static readonly HashSet<int> NoOutline = new HashSet<int>();

        internal static void Tick()
        {
            if (!Plugin.On(Plugin.HighlightStuckScrap)) { if (Shown.Count > 0) Clear(); return; }
            var player = Player.m_localPlayer;
            if (player == null || player.transform.position.y < Plugin.DungeonHeight) { if (Shown.Count > 0) Clear(); return; }

            // only while a pickaxe is in your hand: otherwise hide every glow (kept, so they come back in place)
            var weapon = player.GetCurrentWeapon();
            bool pickaxe = weapon != null && weapon.m_shared != null && weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes;
            _pickaxeNow = pickaxe;
            if (!pickaxe)
            {
                foreach (var m in Shown.Values) Show(m, false);
                return;
            }

            // glints: every 1-2.2 s a small sparkle on a part of the bit you can see, fading in over the last 2 m of
            // HighlightDistance; a hit on the bit sets one off at once, bigger
            float range = Plugin.HighlightDistance.Value;
            Vector3 chest = Plugin.Chest(player);
            Color baseColour = Plugin.HighlightColour.Value;
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            float now = Time.time;
            float speed = Mathf.Max(0.1f, Plugin.HighlightSpeed.Value);
            float glintTime = GlintTime / speed;                          // a faster speed: shorter glints, more often
            foreach (var m in Shown.Values)
            {
                if (m.Edges == null || m.Bit == null || cam == null) continue;
                float fade = Mathf.Clamp01((range - Vector3.Distance(chest, m.Bit.bounds.center)) / 2f);
                if (m.IsGlow) { GlowFrame(m, fade, cam, baseColour, now); continue; }
                if (fade <= 0f || m.Points.Count == 0) { Show(m, false); continue; }
                if (now >= m.FlashUntil - 0.35f && now < m.FlashUntil && m.StartAt < m.FlashUntil - 0.35f)
                { m.StartAt = now; m.Big = true; m.At = m.Points[UnityEngine.Random.Range(0, m.Points.Count)]; }
                else if (now >= m.NextAt)
                {
                    m.StartAt = now; m.Big = false;
                    m.At = m.Points[UnityEngine.Random.Range(0, m.Points.Count)];
                    m.NextAt = now + UnityEngine.Random.Range(1.0f, 2.2f) / speed;
                }
                float age = now - m.StartAt;
                if (age >= glintTime) { Show(m, false); continue; }
                if (Plugin.LogDetails.Value && !m.Logged)
                {
                    m.Logged = true;
                    float d = Vector3.Distance(cam.position, m.At);
                    Plugin.Log.LogInfo(Plugin.LogTag + "Glint on " + m.Bit.name + ": " + m.Points.Count + " visible spots, " +
                        d.ToString("0.0") + " m from the camera; fog " + (RenderSettings.fog ? RenderSettings.fogMode + " density " +
                        RenderSettings.fogDensity.ToString("0.000") + " start " + RenderSettings.fogStartDistance.ToString("0.0") + " end " +
                        RenderSettings.fogEndDistance.ToString("0.0") : "off") + ", made " + FogBoost(d).ToString("0.0") + "x brighter for it.");
                }
                float env = Mathf.Sin(Mathf.PI * age / glintTime);           // rises and falls
                Show(m, true);
                var t = m.Edges.transform;
                // (0.9.10) the scrap you see is drawn a little bigger than the shape the game tests hits against, so a glint
                // on that shape sat inside the visible scrap and was hidden by it. It is now pulled toward you along
                // the line you see that spot on (15% of the way, at least 30 cm, at most half), and made smaller by the
                // same amount, so it looks exactly where and as big as before, but in front of the scrap and the rock.
                Vector3 toSpot = m.At - cam.position;
                float far = toSpot.magnitude;
                float pull = far > 0.01f ? Mathf.Min(Mathf.Max(0.3f, far * 0.15f), far * 0.5f) : 0f;
                float shrink = far > 0.01f ? (far - pull) / far : 1f;
                t.position = far > 0.01f ? m.At - toSpot / far * pull : m.At;
                t.rotation = cam.rotation * Quaternion.Euler(0f, 0f, age * 90f * speed);  // faces you, turning slowly
                t.localScale = Vector3.one * GlintSize * shrink * (m.Big ? 2.2f : 1f) * (0.35f + 0.65f * env);
                float k = env * Plugin.HighlightBrightness.Value * (m.Big ? 1.6f : 0.6f) * (m.Big ? 1f : fade)
                          * FogBoost(Vector3.Distance(cam.position, t.position));   // the crypt fog dims added light
                m.LastK = k; m.LastSize = t.localScale.x;
                if (m.Mat != null)
                {
                    if (_additive) SetColour(m.Mat, new Color(baseColour.r * k, baseColour.g * k, baseColour.b * k, 1f));
                    else SetColour(m.Mat, new Color(baseColour.r, baseColour.g, baseColour.b, Mathf.Clamp01(k)));
                }
            }

            if (Time.time < _nextScan) return;
            _nextScan = Time.time + ScanEvery;
            try { Scan(player); }
            catch (Exception e) { Plugin.Log.LogWarning(Plugin.LogTag + "Highlighting stuck scrap failed: " + e.Message); _nextScan = Time.time + 10f; }
        }

        private static void Scan(Player player)
        {
            if (_pileMask == -1) _pileMask = LayerMask.GetMask("Default");
            if (_blockMask == -1) _blockMask = LayerMask.GetMask("static_solid", "Default", "Default_small", "terrain");
            if (_groundMask == -1) _groundMask = LayerMask.GetMask("static_solid", "Default", "Default_small", "terrain", "piece");
            Vector3 chest = Plugin.Chest(player);

            // which stuck bits are near (nearest first, at most MaxMarkers)
            var want = new List<KeyValuePair<float, Collider>>();
            float scan = Plugin.HighlightDistance.Value + ScanBeyondRange;
            int n = Physics.OverlapSphereNonAlloc(player.transform.position, scan, Found, _pileMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var bit = Piles.BitOf(Found[i]);
                if (bit == null || !bit.enabled || !bit.gameObject.activeInHierarchy) continue;
                // only bits at your own level, within swing height (not ones in another level's floor or ceiling)
                float up = bit.bounds.center.y - player.transform.position.y;
                if (up < -LowestBelowFeet || up > HighestAboveFeet) continue;
                if (!IsStuck(bit) && !LowInRock(bit, player)) continue;
                // hidden scrap counts: a spot to stand within reach of it (worked out once per bit);
                // exposed scrap only (default): it must be in view from your camera right now (checked every second)
                if (Plugin.ReachHiddenScrap.Value ? !CanGetTo(bit) : !InViewNow(bit)) continue;
                want.Add(new KeyValuePair<float, Collider>((bit.bounds.center - chest).sqrMagnitude, bit));
            }
            want.Sort((a, b) => a.Key.CompareTo(b.Key));
            if (want.Count > MaxMarkers) want.RemoveRange(MaxMarkers, want.Count - MaxMarkers);

            if (Plugin.LogDetails.Value || Plugin.DebugText.Value) ExplainCrosshair(player, want);

            var keep = new HashSet<int>();
            foreach (var kv in want)
            {
                var bit = kv.Value;
                int id = bit.GetInstanceID();
                keep.Add(id);
                Marker m;
                if (!Shown.TryGetValue(id, out m) || m.Edges == null)
                {
                    m = Make(bit);
                    if (m == null)
                    {
                        if (Plugin.LogDetails.Value && NoOutline.Add(id))
                            Plugin.Log.LogInfo(Plugin.LogTag + "Could not highlight stuck scrap bit " + bit.name + " (no see-through shader, or no shape to glow).");
                        continue;
                    }
                    m.NextAt = Time.time + UnityEngine.Random.Range(0.1f, 1.2f);   // first glints spread out
                    Shown[id] = m;
                }
                UpdatePoints(m, player);
            }
            var gone = new List<int>();
            foreach (var kv in Shown)
                if (!keep.Contains(kv.Key) || kv.Value.Bit == null || !kv.Value.Bit.enabled || !kv.Value.Bit.gameObject.activeInHierarchy)
                    gone.Add(kv.Key);
            foreach (int id in gone) { Destroy(Shown[id]); Shown.Remove(id); }
        }

        /// A bit is stuck when it touches a jagged block (its centre is in one, or it overlaps one by at least 3 cm)
        /// AND at least half of it is inside the crypt's rock (walls, floor, any room geometry). The second part tells a
        /// sliver wedged into the rock (mostly inside) from the bottom of a pile resting on the floor (mostly in the
        /// open). Worked out once per bit.
        private static bool IsStuck(Collider bit)
        {
            int id = bit.GetInstanceID();
            bool yes;
            if (Stuck.TryGetValue(id, out yes)) return yes;
            yes = false;
            Bounds b = bit.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, Blocking, Quaternion.identity, _blockMask, QueryTriggerInteraction.Ignore);
            bool touches = false;
            for (int i = 0; i < n && !touches; i++)
            {
                var block = Blocking[i];
                if (block == null || Blocks.Of(block) != Blocks.Kind.Jagged) continue;
                if (Inside(block, b.center)) { touches = true; break; }
                Vector3 dir; float dist;
                if (Physics.ComputePenetration(bit, bit.transform.position, bit.transform.rotation,
                                               block, block.transform.position, block.transform.rotation, out dir, out dist)
                    && dist >= Deep)
                    touches = true;
            }
            Touching[id] = touches;
            float buried = touches ? BuriedShare(bit) : 0f;
            yes = touches && buried >= MostlyBuried;
            if (Stuck.Count > 8192) { Stuck.Clear(); Touching.Clear(); }
            Stuck[id] = yes;
            if (yes && Plugin.LogDetails.Value)
            {
                var p = Player.m_localPlayer;
                string where = p != null ? " (" + (b.center.y - p.transform.position.y).ToString("0.0") + " m above your feet, "
                                           + Vector3.Distance(Plugin.Chest(p), b.center).ToString("0.0") + " m from your chest)" : "";
                Plugin.Log.LogInfo(Plugin.LogTag + "Found stuck scrap bit " + bit.name + where + ", " + Mathf.RoundToInt(buried * 100f) + "% in rock.");
            }
            return yes;
        }

        private static readonly Dictionary<int, bool> Touching = new Dictionary<int, bool>();
        private static readonly HashSet<int> LowStuck = new HashSet<int>();   // LowInRock once found true stays true

        /// (0.9.9) Drawn after the crypt's water: most crypt floor scrap is under the water, and the water surface was
        /// drawn over the glints (rock in front still hides them; this only changes the order of see-through things).
        private const int AfterWater = 4000;
        private const float SwingHeight = 1.0f;     // a pickaxe swing's height above the feet, roughly
        private const float SwingWidth = 0.2f;      // the swing's thickness (it is not a thin line)
        private const float StandOff = 1.3f;        // how far from the bit you'd stand to swing at it

        /// (0.9.8, exposed scrap only) A bit that touches a jagged block but is less than half in rock still counts as
        /// stuck when a swing can't get to it: from a spot about 1.3 m away on your side, at swing height, a fat line to
        /// every point on the bit meets rock first (a low sliver in the floor: the swing hits the floor). Another bit
        /// of scrap in the way does not count as blocking (you'd mine through it), so the bottom of a pile is not marked.
        /// Checked every scan, as it depends on where you are.
        private static bool LowInRock(Collider bit, Player player)
        {
            if (Plugin.ReachHiddenScrap.Value) return false;
            if (LowStuck.Contains(bit.GetInstanceID())) return true;
            bool touches;
            if (!Touching.TryGetValue(bit.GetInstanceID(), out touches) || !touches) return false;
            // (0.9.13) a low sliver: its top is less than LowTop above the crypt floor under it, and no other scrap sits
            // on it (so it isn't the bottom of a pile still standing)
            float top = TopAboveFloor(bit);
            if (top >= 0f && top < LowTop && !ScrapAbove(bit))
            {
                if (LowStuck.Count > 8192) LowStuck.Clear();
                LowStuck.Add(bit.GetInstanceID());
                return true;
            }
            Vector3 c = bit.bounds.center;
            Vector3 flat = player.transform.position - c; flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 side = dist > 0.01f ? flat / dist : Vector3.forward;
            Vector3 from = c + side * Mathf.Min(dist, StandOff);
            from.y = player.transform.position.y + SwingHeight;
            foreach (var t in SamplePoints(bit))
            {
                Vector3 dir = t - from;
                float len = dir.magnitude;
                if (len < 0.01f) return false;
                HitList.Clear();
                foreach (var h in Physics.SphereCastAll(from, SwingWidth, dir / len, len + 0.3f, _groundMask, QueryTriggerInteraction.Ignore))
                    if (h.collider != null && !h.collider.transform.IsChildOf(player.transform)) HitList.Add(h);
                HitList.Sort((a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in HitList)
                {
                    if (h.collider == bit || IsPile(h.collider)) return false;   // a swing gets to it (or through scrap to it)
                    break;                                                       // rock first
                }
            }
            if (LowStuck.Count > 8192) LowStuck.Clear();
            LowStuck.Add(bit.GetInstanceID());   // stays marked as you move about it (no flicker)
            return true;
        }

        private const float LowTop = 0.4f;   // a bit whose top is lower than this above the floor: a swing hits the floor

        /// How far the top of the bit is above the crypt floor right under its middle (-1 when no crypt floor is found
        /// within 3 m below).
        internal static float TopAboveFloor(Collider bit)
        {
            Bounds b = bit.bounds;
            Vector3 from = new Vector3(b.center.x, b.max.y + 0.3f, b.center.z);
            bool found = false; float best = 0f;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, b.size.y + 3.3f, _blockMask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || IsPile(h.collider) || !Plugin.InCryptRoom(h.collider)) continue;
                if (!found || h.point.y > best) { best = h.point.y; found = true; }   // the highest crypt surface under it
            }
            return found ? b.max.y - best : -1f;
        }

        /// True when another active scrap bit is right above this one (within 1 m up from its top).
        internal static bool ScrapAbove(Collider bit)
        {
            Bounds b = bit.bounds;
            Vector3 from = new Vector3(b.center.x, b.max.y - 0.05f, b.center.z);
            foreach (var h in Physics.RaycastAll(from, Vector3.up, 1f, _pileMask, QueryTriggerInteraction.Ignore))
                if (h.collider != null && h.collider != bit && h.collider.enabled && h.collider.gameObject.activeInHierarchy && IsPile(h.collider))
                    return true;
            return false;
        }

        // Standing spots tried around a bit: rings at these distances (metres, sideways), 12 directions each.
        private static readonly float[] Rings = { 0.7f, 1.2f, 1.7f, 2.2f };
        private const int Directions = 12;
        private const float BodyRadius = 0.4f, BodyHeight = 1.8f;

        /// Can the player see (point at) part of this bit? True when there is a spot around it with walkable floor and
        /// room for the player's body from which a straight line reaches the bit itself first, within 3 m of the eye.
        /// Worked out once per bit (the rock never moves).
        /// An estimate: it doesn't know whether that spot is connected to where the player is.
        private static bool CanGetTo(Collider bit)
        {
            int id = bit.GetInstanceID();
            bool yes;
            if (Reachable.TryGetValue(id, out yes)) return yes;
            if (_groundMask == -1) _groundMask = LayerMask.GetMask("static_solid", "Default", "Default_small", "terrain", "piece");

            Vector3 c = bit.bounds.center;
            yes = false;
            for (int r = 0; r < Rings.Length && !yes; r++)
                for (int d = 0; d < Directions && !yes; d++)
                {
                    float a = d * Mathf.PI * 2f / Directions;
                    Vector3 above = c + new Vector3(Mathf.Cos(a) * Rings[r], 2.6f, Mathf.Sin(a) * Rings[r]);
                    // the floor under this spot: within 0.6 m above to 2.5 m below the bit (the marker height range)
                    RaycastHit ground;
                    if (!Physics.Raycast(above, Vector3.down, out ground, 5.3f, _groundMask, QueryTriggerInteraction.Ignore)) continue;
                    if (ground.normal.y < 0.7f || IsPile(ground.collider)) continue;
                    float feetY = ground.point.y;
                    if (c.y - feetY < -LowestBelowFeet || c.y - feetY > HighestAboveFeet) continue;
                    Vector3 feet = ground.point;
                    // room for the body
                    Vector3 low = feet + Vector3.up * (BodyRadius + 0.05f), high = feet + Vector3.up * (BodyHeight - BodyRadius);
                    int n = Physics.OverlapCapsuleNonAlloc(low, high, BodyRadius, Body, _groundMask, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int i = 0; i < n && !blocked; i++) if (Body[i] != null && !IsPile(Body[i])) blocked = true;
                    if (blocked) continue;
                    if (Plugin.ReachHiddenScrap.Value)
                    {
                        // hidden scrap counts: within swing reach, and only the last metre may be rock
                        if (HiddenReachFrom(bit, feet + Vector3.up * 1.2f)) yes = true;
                    }
                    // exposed scrap only: some part of the bit can be seen (pointed at) from here, within reach
                    else if (CanSeeFrom(bit, feet + Vector3.up * EyeHeight)) yes = true;
                }
            if (Reachable.Count > 8192) Reachable.Clear();
            Reachable[id] = yes;
            if (!yes && Plugin.LogDetails.Value)
                Plugin.Log.LogInfo(Plugin.LogTag + "No glow for stuck scrap bit " + bit.name + (Plugin.ReachHiddenScrap.Value
                    ? ": no spot to stand within reach of it." : ": no part of it can be seen from a spot you can stand."));
            return yes;
        }

        /// Exposed-scrap mode: true while a straight line from your camera reaches some part of this bit before any
        /// rock, within HighlightDistance. Checked again every scan, so a bit that goes out of view stops glowing.
        private static bool InViewNow(Collider bit)
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return false;
            return CanSeeFrom(bit, cam.position, Plugin.HighlightDistance.Value + 4f);
        }

        private static bool IsPile(Collider c) => c != null && c.GetComponentInParent<MineRock5>() != null;

        private const float SwingReach = 2.2f;   // chest to the bit's surface: about a pickaxe swing

        /// ReachHiddenScrap: the bit is within swing reach of the chest and nothing solid is in between except within
        /// the last 1 m (the part the reach fix covers with ReachHiddenScrap on).
        private static bool HiddenReachFrom(Collider bit, Vector3 chest)
        {
            Vector3 target = ClosestOn(bit, chest);
            float len = Vector3.Distance(chest, target);
            if (len > SwingReach) return false;
            if (len < 0.01f) return true;
            Vector3 dir = (target - chest) / len;
            float firstSolid = len;
            foreach (var h in Physics.RaycastAll(chest, dir, len, _groundMask, QueryTriggerInteraction.Ignore))
                if (h.collider != null && !IsPile(h.collider) && h.distance < firstSolid) firstSolid = h.distance;
            return len - firstSolid <= Plugin.NearbyReach;
        }

        private const float EyeHeight = 1.6f;
        private const float PointReach = 3.0f;   // eye to the bit's surface: about how far the crosshair names scrap

        /// True when a straight line from the eye to some part of the bit meets this bit first (no rock or other
        /// scrap in between), within PointReach. Tries the bit's middle and 14 points around it (corners and faces
        /// of its box, pulled 40% toward the middle so they're on or in the bit).
        private static bool CanSeeFrom(Collider bit, Vector3 eye, float reach = PointReach)
        {
            foreach (var t in SamplePoints(bit))
            {
                Vector3 dir = t - eye;
                float len = dir.magnitude;
                if (len < 0.01f) return true;
                if (len > reach + 0.5f) continue;
                foreach (var h in SortedHits(eye, dir / len, len + 0.3f))
                {
                    var me = Player.m_localPlayer;
                    if (me != null && h.collider.transform.IsChildOf(me.transform)) continue;   // the player's own body
                    if (h.collider == bit && h.distance <= reach) return true;
                    break;                                                                     // something else is in the way
                }
            }
            return false;
        }

        private static readonly List<RaycastHit> HitList = new List<RaycastHit>();

        private static List<RaycastHit> SortedHits(Vector3 from, Vector3 dir, float len)
        {
            HitList.Clear();
            foreach (var h in Physics.RaycastAll(from, dir, len, _groundMask, QueryTriggerInteraction.Ignore))
                if (h.collider != null) HitList.Add(h);
            HitList.Sort((a, b) => a.distance.CompareTo(b.distance));
            return HitList;
        }

        private static readonly Dictionary<int, List<Vector3>> Samples = new Dictionary<int, List<Vector3>>();

        /// Points on the bit's real surface to look at: up to 24 of its mesh corners (spread over the mesh), pulled 3%
        /// toward its middle so a ray ends on the bit and not just past an edge, plus the middle itself. Cached per bit.
        private static List<Vector3> SamplePoints(Collider bit)
        {
            int id = bit.GetInstanceID();
            List<Vector3> list;
            if (Samples.TryGetValue(id, out list)) return list;
            list = new List<Vector3>();
            Vector3 c = bit.bounds.center;
            list.Add(c);
            Mesh mesh = null;
            var mc = bit as MeshCollider;
            if (mc != null) mesh = mc.sharedMesh;
            if (mesh != null && mesh.isReadable)
            {
                var v = mesh.vertices;
                int step = Mathf.Max(1, v.Length / 24);
                for (int k = 0; k < v.Length; k += step)
                {
                    Vector3 w = bit.transform.TransformPoint(v[k]);
                    list.Add(Vector3.Lerp(w, c, 0.03f));
                }
            }
            else
            {
                Vector3 e = bit.bounds.extents * 0.6f;
                for (int k = 0; k < 8; k++)
                    list.Add(c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z));
            }
            if (Samples.Count > 8192) Samples.Clear();
            Samples[id] = list;
            return list;
        }

        /// The point on the bit's real surface facing p. Concave mesh colliders have no ClosestPoint, and their bounding
        /// box sticks out into open air, so a ray from p toward the bit's centre is cast against this collider only.
        internal static Vector3 ClosestOn(Collider c, Vector3 p)
        {
            var mesh = c as MeshCollider;
            if (mesh == null || mesh.convex) return c.ClosestPoint(p);
            Vector3 centre = c.bounds.center;
            Vector3 dir = centre - p;
            float len = dir.magnitude;
            if (len < 0.001f) return centre;
            RaycastHit hit;
            return c.Raycast(new Ray(p, dir / len), out hit, len + 0.01f) ? hit.point : centre;
        }

        // See-through shaders the game may have, best first. Shader.Find only finds shaders the build included, so the
        // loaded shaders are searched by name too. Additive ones (light added, nothing darkened) suit a glow best.
        private static readonly string[] AdditiveShaders =
            { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive", "Legacy Shaders/Particles/Additive (Soft)", "Particles/Additive (Soft)" };
        private static readonly string[] BlendedShaders =
            { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Sprites/Default", "UI/Default" };

        private static Shader GlowShader()
        {
            if (_glowShader != null) return _glowShader;
            Shader[] loaded = null;
            foreach (var group in new[] { AdditiveShaders, BlendedShaders })
                foreach (string name in group)
                {
                    var sh = Shader.Find(name);
                    if (sh == null)
                    {
                        if (loaded == null) loaded = Resources.FindObjectsOfTypeAll<Shader>();
                        foreach (var l in loaded) if (l != null && l.name == name) { sh = l; break; }
                    }
                    if (sh == null || !sh.isSupported) continue;
                    _glowShader = sh;
                    _additive = group == AdditiveShaders;
                    Plugin.Log.LogInfo(Plugin.LogTag + "Stuck scrap glow drawn with shader \"" + name + "\" (" + (_additive ? "adds light" : "see-through blend") + ").");
                    return _glowShader;
                }
            Plugin.Log.LogWarning(Plugin.LogTag + "No see-through shader found for the stuck scrap glow; no glow is drawn.");
            return null;
        }

        private static void SetColour(Material mat, Color c)
        {
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        private static readonly Dictionary<int, string> Explained = new Dictionary<int, string>();
        private static string _crosshairWhy = "";
        private static bool _pickaxeNow;
        private static GUIStyle _dbgStyle;

        /// DebugText: a panel at the top left and a label on each marked bit, drawn over everything.
        internal static void DebugGui()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (_dbgStyle == null)
            {
                _dbgStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                _dbgStyle.normal.textColor = Color.white;
            }
            var cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : null;
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>Unreachable Scrap debug</b>  in crypt: ").Append(player.transform.position.y >= Plugin.DungeonHeight)
              .Append("  pickaxe: ").Append(_pickaxeNow).Append("  style: ").Append(Plugin.Style.Value)
              .Append("  shader: ").Append(_glowShader != null ? _glowShader.name : "none").Append(_additive ? " (adds)" : "")
              .Append("  marked: ").Append(Shown.Count).Append('\n');
            sb.Append("crosshair: ").Append(_crosshairWhy).Append('\n');
            int lines = 0;
            foreach (var m in Shown.Values)
            {
                if (m.Bit == null || lines++ >= 10) continue;
                string id = ShortName(m.Bit);
                bool on = m.Edges != null && m.Edges.activeInHierarchy;
                var r = m.Edges != null ? m.Edges.GetComponent<Renderer>() : null;
                sb.Append(id).Append(": ").Append(m.Points.Count).Append(" spots, ")
                  .Append(cam != null ? Vector3.Distance(cam.transform.position, m.Bit.bounds.center).ToString("0.0") : "?").Append(" m, ")
                  .Append(on ? "<color=lime>GLINT ON</color> k=" + m.LastK.ToString("0.00") + " size=" + m.LastSize.ToString("0.00")
                             : "off, next in " + Mathf.Max(0f, m.NextAt - Time.time).ToString("0.0") + " s")
                  .Append(r != null ? "  renderer " + (r.enabled ? "on" : "OFF") + " q" + (m.Mat != null ? m.Mat.renderQueue.ToString() : "?") +
                                       " layer " + LayerMask.LayerToName(m.Edges.layer) : "  no renderer")
                  .Append('\n');
            }
            float top = Mathf.Max(220f, Screen.height * 0.22f);   // below the hotbar
            GUI.Box(new Rect(8, top, 900, 22 + 18 * (2 + Mathf.Min(lines, 10))), "");
            GUI.Label(new Rect(14, top + 2, 890, 400), sb.ToString(), _dbgStyle);
            if (cam == null) return;
            foreach (var m in Shown.Values)
            {
                if (m.Bit == null) continue;
                foreach (var p in m.Points) DbgMark(cam, p, "<color=yellow>+</color>");
                DbgMark(cam, m.Bit.bounds.center, "<color=cyan>" + ShortName(m.Bit) + "</color>");
                if (m.Edges != null && m.Edges.activeInHierarchy) DbgMark(cam, m.Edges.transform.position, "<color=red><b>*</b></color>");
            }
        }

        private static string ShortName(Collider bit)
        {
            string n = bit.name; int dot = n.LastIndexOf('.');
            return dot >= 0 ? n.Substring(dot + 1) : n;
        }

        private static void DbgMark(Camera cam, Vector3 world, string text)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            GUI.Label(new Rect(sp.x - 6, Screen.height - sp.y - 10, 120, 22), text, _dbgStyle);
        }

        /// LogDetails only: if the crosshair is on a crypt scrap bit, say once (and again when it changes) whether it
        /// glows and if not, why.
        private static void ExplainCrosshair(Player player, List<KeyValuePair<float, Collider>> marked)
        {
            _crosshairWhy = "not on scrap";
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return;
            RaycastHit hit;
            if (!Physics.Raycast(cam.position, cam.forward, out hit, 12f, Plugin.SolidAndPileMask, QueryTriggerInteraction.Ignore)) return;
            var bit = Piles.BitOf(hit.collider);
            if (bit == null) return;
            string why;
            bool glows = false;
            foreach (var kv in marked) if (kv.Value == bit) { glows = true; break; }
            float up = bit.bounds.center.y - player.transform.position.y;
            if (glows)
            {
                Marker shown;
                why = Shown.TryGetValue(bit.GetInstanceID(), out shown) ? "glows (" + shown.Points.Count + " visible spots)" : "glows (new)";
            }
            else if (Vector3.Distance(Plugin.Chest(player), bit.bounds.center) > Plugin.HighlightDistance.Value + ScanBeyondRange) why = "too far away";
            else if (up < -LowestBelowFeet || up > HighestAboveFeet) why = "not at your level (" + up.ToString("0.0") + " m from your feet)";
            else if (!IsStuck(bit) && !LowInRock(bit, player)) why = "not stuck (" + Mathf.RoundToInt(BuriedShare(bit) * 100f) + "% in rock, and a swing can get to it; top " + TopAboveFloor(bit).ToString("0.00") + " m above the floor, scrap on it: " + (ScrapAbove(bit) ? "yes" : "no") + "; blocks it touches: " + TouchingBlocks(bit) + ")";
            else if (Plugin.ReachHiddenScrap.Value ? !CanGetTo(bit) : !InViewNow(bit))
                why = Plugin.ReachHiddenScrap.Value ? "stuck, but no spot to stand within reach of it" : "stuck, but no line from your camera reaches it before rock";
            else why = "not among the nearest " + MaxMarkers;
            _crosshairWhy = ShortName(bit) + ": " + why;
            if (!Plugin.LogDetails.Value) return;
            string before;
            int id = bit.GetInstanceID();
            if (Explained.TryGetValue(id, out before) && before == why) return;
            Explained[id] = why;
            Plugin.Log.LogInfo(Plugin.LogTag + "Crosshair on scrap bit " + bit.name + ": " + why + ".");
        }

        private static string TouchingBlocks(Collider bit)
        {
            Bounds b = bit.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.05f, Blocking, Quaternion.identity, _blockMask, QueryTriggerInteraction.Ignore);
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var c = Blocking[i];
                if (c == null || IsPile(c)) continue;
                string nm = Plugin.ParentName(c) + "/" + c.name + (Blocks.Of(c) == Blocks.Kind.Jagged ? " (jagged)" : "");
                if (!names.Contains(nm)) names.Add(nm);
            }
            return names.Count == 0 ? "none" : string.Join(", ", names.ToArray());
        }

        /// Style Glow, every frame: a soft pulse (between about a fifth and two fifths of full strength), brighter for a
        /// moment when a swing is handed to the bit. Rock in front of the bit hides the glow on its buried part.
        private static void GlowFrame(Marker m, float fade, Transform cam, Color colour, float now)
        {
            if (fade <= 0f) { Show(m, false); return; }
            float pulse = 0.5f + 0.5f * Mathf.Sin((now * Mathf.Max(0.1f, Plugin.HighlightSpeed.Value) / GlowPulse + m.Phase) * 2f * Mathf.PI);
            float k = (0.18f + 0.22f * pulse) * fade;
            if (now < m.FlashUntil) k += 1.2f * (m.FlashUntil - now) / 0.35f;
            k *= Plugin.HighlightBrightness.Value * FogBoost(Vector3.Distance(cam.position, m.Bit.bounds.center));
            Show(m, true);
            if (m.Mat == null) return;
            if (_additive) SetColour(m.Mat, new Color(colour.r * k, colour.g * k, colour.b * k, 1f));
            else SetColour(m.Mat, new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(k)));
        }

        /// Style Glow: a copy of the bit's own shape (white vertex colours), 2% larger about its middle, laid over it.
        /// It hangs under the bit, so it moves, hides and goes with it.
        private static Marker MakeGlow(Collider bit, Shader shader)
        {
            var mc = bit as MeshCollider;
            if (mc == null || mc.sharedMesh == null) return null;
            var mat = new Material(shader) { name = "WolvysUnreachableScrap_Glow", renderQueue = AfterWater };
            SetColour(mat, Color.clear);
            var go = new GameObject("WolvysUnreachableScrap_StuckScrapGlow");
            go.transform.SetParent(bit.transform, false);
            Vector3 mid = mc.sharedMesh.bounds.center;
            go.transform.localPosition = mid * (1f - GlowGrow);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * GlowGrow;
            go.AddComponent<MeshFilter>().sharedMesh = GlowMesh(mc.sharedMesh);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return new Marker { Bit = bit, Edges = go, Mat = mat, IsGlow = true, Phase = UnityEngine.Random.value };
        }

        private static void Show(Marker m, bool on)
        {
            if (m.Edges != null && m.Edges.activeSelf != on) m.Edges.SetActive(on);
        }

        /// The glint: a small four-pointed sparkle (a camera-facing square with a star picture), drawn with the game's
        /// light-adding shader and depth, so rock in front of it hides it. Hidden until Tick shows one.
        private static Marker Make(Collider bit)
        {
            var shader = GlowShader();
            if (shader == null) return null;
            if (Plugin.Style.Value == Plugin.HighlightStyle.Glow) return MakeGlow(bit, shader);
            var mat = new Material(shader) { name = "WolvysUnreachableScrap_Glint", mainTexture = Star(), renderQueue = AfterWater };
            SetColour(mat, Color.clear);

            var go = new GameObject("WolvysUnreachableScrap_StuckScrapGlint");
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return new Marker { Bit = bit, Edges = go, Mat = mat };
        }

        /// The spots on the bit a glint can appear: where a line from your camera meets the bit itself (exposed scrap),
        /// or, with ReachHiddenScrap on, also the side of the bit facing you.
        private static void UpdatePoints(Marker m, Player player)
        {
            m.Points.Clear();
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam != null)
            {
                Vector3 eye = cam.position;
                foreach (var t in SamplePoints(m.Bit))
                {
                    Vector3 dir = t - eye;
                    float len = dir.magnitude;
                    if (len < 0.01f) continue;
                    foreach (var h in SortedHits(eye, dir / len, len + 0.3f))
                    {
                        if (h.collider.transform.IsChildOf(player.transform)) continue;
                        if (h.collider == m.Bit) m.Points.Add(h.point);
                        break;
                    }
                }
            }
            if (m.Points.Count == 0 && Plugin.ReachHiddenScrap.Value) m.Points.Add(ClosestOn(m.Bit, Plugin.Chest(player)));
        }

        /// How much brighter a glint at this distance must be to look the same through the fog. The game's fog mixes
        /// added light toward nothing by the fog amount, so the glint is divided by what gets through (at most 8x).
        private static float FogBoost(float distance)
        {
            if (!RenderSettings.fog) return 1f;
            float through;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    float span = Mathf.Max(0.01f, RenderSettings.fogEndDistance - RenderSettings.fogStartDistance);
                    through = Mathf.Clamp01((RenderSettings.fogEndDistance - distance) / span);
                    break;
                case FogMode.Exponential:
                    through = Mathf.Exp(-RenderSettings.fogDensity * distance);
                    break;
                default: // ExponentialSquared
                    float x = RenderSettings.fogDensity * distance;
                    through = Mathf.Exp(-x * x);
                    break;
            }
            return Mathf.Min(8f, 1f / Mathf.Max(0.125f, through));
        }

        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "WolvysUnreachableScrap_GlintQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };   // both sides
            _quad.RecalculateBounds();
            return _quad;
        }

        /// A 64x64 star: a soft round core with four thin rays (white, brightness in alpha).
        private static Texture2D Star()
        {
            if (_star != null) return _star;
            const int size = 64;
            _star = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "WolvysUnreachableScrap_Star", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Exp(-r * r * 30f);
                    float rays = Mathf.Exp(-Mathf.Abs(v) * 45f) * Mathf.Clamp01(1f - Mathf.Abs(u))
                               + Mathf.Exp(-Mathf.Abs(u) * 45f) * Mathf.Clamp01(1f - Mathf.Abs(v));
                    float a = Mathf.Clamp01(core + rays * 0.8f) * Mathf.Clamp01(1f - r * 0.9f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            _star.SetPixels32(px);
            _star.Apply(false, true);
            return _star;
        }

        /// A swing was handed to this bit: it gives a big glint at once.
        internal static void Flash(Collider bit)
        {
            Marker m;
            if (bit != null && Shown.TryGetValue(bit.GetInstanceID(), out m)) m.FlashUntil = Time.time + 0.35f;
        }

        internal static void Recolour() { }   // the colour is applied every frame in Tick

        private static void Destroy(Marker m)
        {
            if (m == null) return;
            if (m.Edges != null) UnityEngine.Object.Destroy(m.Edges);
            if (m.Mat != null) UnityEngine.Object.Destroy(m.Mat);
        }

        /// Remove every marker (setting off, left the dungeon, mod unloaded).
        internal static void Clear()
        {
            foreach (var m in Shown.Values) Destroy(m);
            Shown.Clear();
            _nextScan = 0f;
        }

        /// The jagged block list changed: work out again which bits are stuck.
        internal static void Forget()
        {
            Stuck.Clear();
            Touching.Clear();
            LowStuck.Clear();
            Reachable.Clear();
            Samples.Clear();
            Clear();
        }
    }
}
