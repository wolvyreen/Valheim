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
//   full (optional)    - Func<string, object, bool> (hub 4): a section shown on the whole window. Called with the
//                        section and an empty RectTransform covering the window below the tab row; return true when
//                        the mod built its panel there (the window hides the section list and shows a Back button),
//                        false to show the section as usual
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
        /// 2: tabs show the mod's name without "Wolvy's" in front (changed in Map Pins Extras 3.1.1).
        /// 3: Hub.Open; the inventory ignores keys while any text box in the window has the keyboard (Drop Counters 2.11.0).
        /// 4: the window can be resized (corner grip, size kept); a mod's section can fill the whole window with a Back
        ///    button ("full", Drop Counters 2.11.1).
        /// 5: the window can be moved by dragging it (title, frame or empty space); the place is kept (Drop Counters 2.11.2).
        /// 6: on a whole-window section the mod tabs stay in view, with Settings (the mod's sections) instead of Back
        ///    (Drop Counters 2.12.0).
        /// 7: resizing keeps the top-left corner (normal window resize); X close button at the top right; no Settings
        ///    button in the tab row (Drop Counters 2.12.1).
        /// 8: HubKit.SettingRow keeps its fixed width in a wide window (controls lined up with the row's line again)
        ///    (Drop Counters 2.12.2).
        /// 9: reset arrow left of the control, slider value left of the slider: controls line up on the right (Drop Counters 2.12.3).
        /// 10: setting lines stretch with the window again (line too); slider bars, drop-downs and text boxes one width (Drop Counters 2.12.4).
        internal const int HubVersion = 10;
        internal const string ListKey = "Wolvyreen.ModHub.1";
        /// (hub 3) The copy that draws puts its "open the window at this mod's tab" action here (Action&lt;string, string&gt;:
        /// mod id, section or null), so any mod can open the window at its own tab, whichever copy draws.
        internal const string OpenKey = "Wolvyreen.ModHub.Open";

        /// (hub 3) Opens the inventory and the Wolvy's Mods window at a mod's tab (and section, when given).
        /// False when no copy that can do this is drawing (an older hub draws, or no hub at all).
        internal static bool Open(string id, string section = null)
        {
            var open = AppDomain.CurrentDomain.GetData(OpenKey) as Action<string, string>;
            if (open == null)
                return false;
            open(id, section);
            return true;
        }

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
            Func<ConfigEntryBase, object, float> rows = null, Action<object> page = null, Func<string, object, bool> full = null)
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
                    { "changed", (Action)Check }, { "rows", rows }, { "page", page }, { "full", full },
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
