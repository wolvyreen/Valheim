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
        /// 11-13: slider clicks, drop-down text, setting rows grow, stray hub buttons removed (Unreachable Scrap 0.9.x).
        /// 14: About page (an "i" button left of the X): Wolvyreen's published mods from Catalog, installed or not, with
        ///     Thunderstore / Nexus / GitHub buttons (Unreachable Scrap 1.0.0).
        internal const int HubVersion = 14;
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

    /// (hub 14) Wolvyreen's PUBLISHED mods, for the About page (an "i" button next to the X). Only released mods go here
    /// (Wolvyreen, 2026-11-10): when a mod is first released, add it here (and raise Hub.HubVersion) in every mod.
    /// Id = the mod's BepInPlugin GUID, so the page can tell whether it is installed. An empty link hides its button.
    internal static class Catalog
    {
        internal const string GitHub = "https://github.com/wolvyreen/Valheim";

        internal sealed class Mod
        {
            internal string Id, Name, About, Thunderstore, Nexus, IconPng;
        }

        internal static readonly Mod[] Mods =
        {
            new Mod
            {
                Id = "wolvy.valheim.crossbeams",
                Name = "Wolvy's Cross Beams",
                About = "Lets two of the same angled beam snap into an X. Vanilla refuses the second one; with this it clicks into place. Real duplicates are still blocked.",
                Thunderstore = "https://thunderstore.io/c/valheim/p/Wolvyreen/Wolvys_Cross_Beams/",
                Nexus = "https://www.nexusmods.com/valheim/mods/4876",
                IconPng = "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",
            },
            new Mod
            {
                Id = "wolvy.valheim.unreachablescrap",
                Name = "Wolvy's Unreachable Scrap",
                About = "Mine the muddy scrap stuck in Sunken Crypt walls and floors: point at it and swing. A little glint shows you where stuck scrap is, and loot that would fall out of the world lands at your feet.",
                Thunderstore = "https://thunderstore.io/c/valheim/p/Wolvyreen/Wolvys_Unreachable_Scrap/",
                Nexus = "",
                IconPng = "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",
            },
        };
    }
}
