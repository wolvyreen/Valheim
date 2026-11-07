// Wolvyreen Mod Hub - core (shared: the same file, unchanged, goes into every one of Wolvyreen's mods)
//
// One button in the inventory opens one window for all of Wolvyreen's mods that are installed,
// with a tab per mod. There is no separate hub mod: every mod carries this file (and the window,
// ModHubUi/HubWindow.cs). Each copy registers its mod in a list kept in the AppDomain, which all
// copies share. The copy with the highest HubVersion (ties: the lowest mod id) draws the button and
// the window; the others only add their mod to the list. When a mod comes or goes (ScriptEngine
// reload, a mod removed), every copy checks again who draws.
//
// What a mod gives (all plain .NET or BepInEx types, so copies in different DLLs understand each other):
//   id, name, version  - its BepInEx GUID, display name and version
//   config             - its BepInEx ConfigFile: the window shows every setting of it, game-styled
//   rows (optional)    - Func<ConfigEntryBase, object, float>: draws a setting itself (parent is a
//                        UnityEngine.RectTransform); returns the height used, or a negative number to
//                        leave the setting to the window
//   page (optional)    - Action<object>: draws extra content at the top of the mod's tab (parent RectTransform)
//
// This file needs only System and BepInEx, so it can be tested without the game.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace WolvyreenModHub
{
    internal static class Hub
    {
        /// The window's version. Raise it whenever HubWindow.cs changes, so the newest window wins.
        internal const int HubVersion = 1;
        internal const string ListKey = "Wolvyreen.ModHub.1";

        internal static ManualLogSource Log;
        /// Set by the window (HubWindow.Install): start or stop drawing the button and window.
        internal static Action<bool> Host;
        internal static bool Hosting { get; private set; }

        private static Dictionary<string, object> _mine;

        /// The shared list: every registered mod, from every copy of this file.
        internal static List<Dictionary<string, object>> Entries()
        {
            var list = AppDomain.CurrentDomain.GetData(ListKey) as List<Dictionary<string, object>>;
            if (list == null)
            {
                list = new List<Dictionary<string, object>>();
                AppDomain.CurrentDomain.SetData(ListKey, list);
            }
            return list;
        }

        /// Adds this mod to the hub. Call once, after every setting is bound.
        internal static void Register(string id, string name, string version, ConfigFile config, ManualLogSource log, Harmony harmony,
            Func<ConfigEntryBase, object, float> rows = null, Action<object> page = null)
        {
            Log = log;
            try
            {
                Type window = typeof(Hub).Assembly.GetType("WolvyreenModHub.HubWindow");
                bool canDraw = false;
                if (window != null && harmony != null)
                {
                    MethodInfo install = window.GetMethod("Install", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    canDraw = install != null && (bool)install.Invoke(null, new object[] { harmony });
                }

                List<Dictionary<string, object>> list = Entries();
                var earlier = list.Where(e => Get<string>(e, "id") == id).ToList();   // an older copy of this same mod
                foreach (Dictionary<string, object> e in earlier)
                    list.Remove(e);
                _mine = new Dictionary<string, object>
                {
                    { "id", id }, { "name", name }, { "version", version }, { "config", config },
                    { "hubVersion", HubVersion }, { "canDraw", canDraw },
                    { "changed", (Action)Check }, { "rows", rows }, { "page", page },
                };
                list.Add(_mine);
                Notify(earlier);
                log?.LogInfo($"Mod hub: {name} added ({list.Count} of Wolvyreen's mods in the hub; drawn by {Get<string>(Leader(), "name") ?? "none"}).");
            }
            catch (Exception e)
            {
                log?.LogWarning($"Mod hub: could not add {name}: {e.Message}");
            }
        }

        /// Takes this mod out of the hub (the mod is unloading).
        internal static void Unregister()
        {
            try
            {
                if (_mine != null)
                {
                    Entries().Remove(_mine);
                    _mine = null;
                }
                SetHosting(false);
                Notify(null);
            }
            catch (Exception e)
            {
                Log?.LogWarning($"Mod hub: cleanup failed: {e.Message}");
            }
        }

        /// The copy that draws: the highest window version among the copies that have a window; ties go to the lowest id.
        internal static Dictionary<string, object> Leader()
        {
            return Entries().Where(e => Get<bool>(e, "canDraw"))
                .OrderByDescending(e => Get<int>(e, "hubVersion"))
                .ThenBy(e => Get<string>(e, "id"), StringComparer.Ordinal)
                .FirstOrDefault();
        }

        /// Every registered mod, in tab order (by name).
        internal static List<Dictionary<string, object>> Mods()
        {
            return Entries().OrderBy(e => Get<string>(e, "name"), StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void Check()
        {
            SetHosting(_mine != null && ReferenceEquals(Leader(), _mine));
        }

        private static void SetHosting(bool on)
        {
            if (Hosting == on)
                return;
            Hosting = on;
            try
            {
                Host?.Invoke(on);
            }
            catch (Exception e)
            {
                Log?.LogWarning($"Mod hub: switching the window {(on ? "on" : "off")} failed: {e.Message}");
            }
        }

        /// Tells every copy (and the ones just taken out) to check who draws.
        private static void Notify(IEnumerable<Dictionary<string, object>> removed)
        {
            var all = Entries().ToList();
            if (removed != null)
                all.AddRange(removed);
            foreach (Dictionary<string, object> e in all)
            {
                try
                {
                    Get<Action>(e, "changed")?.Invoke();
                }
                catch (Exception ex)
                {
                    Log?.LogWarning($"Mod hub: {Get<string>(e, "name")} could not check in: {ex.Message}");
                }
            }
        }

        internal static T Get<T>(Dictionary<string, object> e, string key)
        {
            if (e != null && e.TryGetValue(key, out object v) && v is T t)
                return t;
            return default(T);
        }
    }
}
