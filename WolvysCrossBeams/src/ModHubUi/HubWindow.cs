// Wolvyreen Mod Hub - the window (shared: the same file, unchanged, goes into every one of Wolvyreen's mods)
//
// ONE button in the inventory, shared by all of Wolvyreen's mods (a copy of the game's Trophies button
// with a wolf's head; it takes the place Wolvy's Drop Counters' own button had) opens a window in the
// inventory screen, like the game's Trophies window. It is a copy of that window: the same wood frame,
// title, close button and scroll list. Along the top is a tab for each of Wolvyreen's mods that is
// installed; a tab shows that mod's settings, grouped by section, with controls copied from the game's
// own Settings menu (checkboxes, sliders, drop-downs, text boxes) and the game's fonts and tooltips.
// Changes are saved at once, like Configuration Manager does.
//
// Only one copy draws (see WolvyreenModHub.cs). The game is reached by reflection; Unity's UI and
// TextMeshPro are used directly. Everything not found (a game update) falls back to something plainer,
// and what was found is written to the log once, to make fixing it easy.
// Needs: UnityEngine.UI, UnityEngine.UIModule, Unity.TextMeshPro, UnityEngine.InputLegacyModule.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WolvyreenModHub
{
    internal static class HubWindow
    {
        private const string Title = "Wolvy's Mods";
        internal static readonly Color Beige = new Color(0.94f, 0.88f, 0.75f, 1f);
        internal static readonly Color Muted = new Color(0.72f, 0.67f, 0.58f, 1f);
        internal static readonly Color Orange = new Color(1f, 0.63f, 0.24f, 1f);

        private static bool _installed;
        private static FieldInfo _guiInstance, _trophiesPanel, _infoPanel, _tabCraft, _menuInstance, _settingsPrefab;
        private static Type _tooltipType;
        private static FieldInfo _tipPrefab, _tipTopic, _tipText;

        private static GameObject _button, _panel, _holder;
        private static RectTransform _content, _tabs, _side;
        private static readonly Dictionary<string, string> Sections = new Dictionary<string, string>();   // mod id -> section shown
        private static readonly Dictionary<ConfigEntryBase, GameObject> Resets = new Dictionary<ConfigEntryBase, GameObject>();
        private static readonly HashSet<ConfigFile> Watched = new HashSet<ConfigFile>();
        private static string _tab;
        private static readonly List<KeyValuePair<RectTransform, Vector2>> Moved = new List<KeyValuePair<RectTransform, Vector2>>();
        internal static readonly List<TMP_InputField> Fields = new List<TMP_InputField>();
        private static Sprite _icon;
        private static GameObject _legacy;   // Wolvy's Drop Counters' own button (from before the hub): hidden, there is one button for all
        private const string LegacyButton = "WolvysDropCountersButton";
        private static bool _failedOnce, _reported;

        // templates (found once per game session)
        internal static Toggle _toggleT;
        internal static Slider _sliderT;
        internal static TMP_Dropdown _dropT;
        internal static TMP_InputField _inputT;
        internal static Button _textButtonT, _tabT;
        internal static TMP_FontAsset _bodyFont, _headFont;
        private static GameObject _tipPrefabObj;

        private static GameObject _kitHolder;

        /// An inactive place to make copies of the game's controls in, so none of their scripts run before
        /// the game-only ones are taken off.
        internal static Transform Holder()
        {
            if (_kitHolder == null)
            {
                _kitHolder = new GameObject("WolvyreenModHubHolder");
                _kitHolder.SetActive(false);
                Object.DontDestroyOnLoad(_kitHolder);
            }
            return _kitHolder.transform;
        }

        /// The game's parts are found (needed by every control; a mod's own rows may run in a copy that does not draw).
        internal static bool Ready()
        {
            if (_tabT != null && _tabT)
                return true;
            var gui = _guiInstance?.GetValue(null) as Component;
            if (gui == null)
                return false;
            FindTemplates(gui);
            return _tabT != null;
        }

        /// Called by Hub.Register. Returns whether this copy can draw the window.
        internal static bool Install(Harmony harmony)
        {
            if (_installed)
                return true;
            Type gui = AccessTools.TypeByName("InventoryGui");
            Type menu = AccessTools.TypeByName("Menu");
            if (gui == null)
                return false;
            _guiInstance = AccessTools.Field(gui, "m_instance");
            _trophiesPanel = AccessTools.Field(gui, "m_trophiesPanel");
            _infoPanel = AccessTools.Field(gui, "m_infoPanel");
            _tabCraft = AccessTools.Field(gui, "m_tabCraft");
            _menuInstance = menu != null ? AccessTools.Field(menu, "m_instance") : null;
            _settingsPrefab = menu != null ? AccessTools.Field(menu, "m_settingsPrefab") : null;
            _tooltipType = AccessTools.TypeByName("UITooltip");
            if (_tooltipType != null)
            {
                _tipPrefab = AccessTools.Field(_tooltipType, "m_tooltipPrefab");
                _tipTopic = AccessTools.Field(_tooltipType, "m_topic");
                _tipText = AccessTools.Field(_tooltipType, "m_text");
            }
            MethodInfo show = AccessTools.Method(gui, "Show");
            MethodInfo hide = AccessTools.Method(gui, "Hide");
            MethodInfo update = AccessTools.Method(gui, "Update");
            if (_trophiesPanel == null || show == null || hide == null || update == null)
                return false;
            // last, so other mods' inventory buttons (Drop Counters') already exist when ours is placed
            harmony.Patch(show, postfix: new HarmonyMethod(typeof(HubWindow), nameof(ShowPostfix)) { priority = Priority.Last });
            harmony.Patch(hide, postfix: new HarmonyMethod(typeof(HubWindow), nameof(HidePostfix)));
            harmony.Patch(update, prefix: new HarmonyMethod(typeof(HubWindow), nameof(UpdatePrefix)));
            Hub.Host = SetHosting;
            _installed = true;
            return true;
        }

        private static void SetHosting(bool on)
        {
            if (!on)
            {
                Remove();
                return;
            }
            var gui = _guiInstance?.GetValue(null) as Component;
            if (gui != null && gui.gameObject.activeInHierarchy)
                Safe(() => EnsureButton(gui));
        }

        // ---------- hooks ----------

        private static void ShowPostfix(object __instance)
        {
            if (!Hub.Hosting)
                return;
            Safe(() => EnsureButton(__instance as Component));
        }

        private static void HidePostfix()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }

        /// While a text box of ours has the keyboard, the inventory must not react to keys (E, Tab, Esc...).
        /// Esc closes our window first, like the game's Trophies window.
        private static bool UpdatePrefix()
        {
            if (!Hub.Hosting || _panel == null || !_panel.activeSelf)
                return true;
            if (Typing())
                return false;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return false;
            }
            return true;
        }

        private static bool Typing()
        {
            foreach (TMP_InputField f in Fields)
                if (f != null && f.isFocused)
                    return true;
            return false;
        }

        // ---------- the button ----------

        private static void EnsureButton(Component gui)
        {
            if (gui == null)
                return;
            Button trophies = FindByMethod(gui, "OnOpenTrophies");
            if (_button != null && _button)
            {
                if (trophies != null)
                {
                    HideLegacy(trophies.transform.parent);
                    LayoutRow(trophies.transform.parent);
                }
                return;
            }
            if (trophies == null)
            {
                Hub.Log?.LogWarning("Mod hub: the inventory's Trophies button was not found, so there is no hub button.");
                return;
            }
            _button = Object.Instantiate(trophies.gameObject, trophies.transform.parent);
            _button.name = "WolvyreenModHubButton";
            var b = _button.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() => Safe(Open));
            _button.transform.SetAsLastSibling();
            HideLegacy(trophies.transform.parent);
            LayoutRow(trophies.transform.parent);

            Image icon = IconOf(trophies);
            if (icon != null)
            {
                string path = PathTo(icon.transform, trophies.transform);
                Transform t = path.Length == 0 ? _button.transform : _button.transform.Find(path);
                Image img = t != null ? t.GetComponent<Image>() : null;
                if (img != null)
                {
                    img.sprite = Icon();
                    img.preserveAspect = true;
                }
            }
            SetTooltip(_button, Title, "Settings for each of Wolvyreen's mods installed");
        }

        /// One button for all: Drop Counters (a version from before the hub) adds its own; hide it every time.
        private static void HideLegacy(Transform row)
        {
            if (row == null)
                return;
            Transform legacy = row.Find(LegacyButton);
            if (legacy == null)
                return;
            _legacy = legacy.gameObject;
            if (_legacy.activeSelf)
                _legacy.SetActive(false);
        }

        /// Spaces the visible buttons of the row (Texts, Skills, Trophies, ..., ours last) evenly, centred on the
        /// board. Done on every inventory opening, so nothing another mod did before (or a button hidden) leaves a gap.
        private static void LayoutRow(Transform parent)
        {
            var board = parent as RectTransform;
            var mine = _button != null ? (RectTransform)_button.transform : null;
            List<RectTransform> row = parent.Cast<Transform>()
                .Where(t => t != mine && t.gameObject.activeSelf && (t.GetComponent<Button>() != null || t.GetComponent<Toggle>() != null))
                .Cast<RectTransform>().OrderBy(t => t.localPosition.x).ToList();
            if (mine != null)
                row.Add(mine);
            if (board == null || row.Count < 2)
                return;
            foreach (RectTransform t in row)
                if (t != mine && !Moved.Any(kv => kv.Key == t))
                    Moved.Add(new KeyValuePair<RectTransform, Vector2>(t, t.anchoredPosition));   // put back when the hub goes
            int n = row.Count;
            float buttonW = Mathf.Max(1f, row.Max(t => t.rect.width));
            float boardW = board.rect.width > 1f ? board.rect.width : (buttonW + 40f) * n;
            const float margin = 45f;   // from the board's edge to the outer buttons
            float step = Mathf.Min(buttonW * 1.6f, (boardW - buttonW - 2f * margin) / (n - 1));
            float centre = board.rect.center.x;
            float y = row[0].localPosition.y;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = row[i].localPosition;
                row[i].localPosition = new Vector3(centre + (i - (n - 1) / 2f) * step, i == n - 1 && row[i] == mine ? y : p.y, p.z);
            }
        }

        private static Button FindByMethod(Component gui, string method)
        {
            var root = _infoPanel?.GetValue(gui) as Transform ?? gui.transform;
            foreach (Button b in root.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentMethodName(i) == method)
                        return b;
            return null;
        }

        private static Image IconOf(Button b)
        {
            Image[] images = b.GetComponentsInChildren<Image>(true).Where(i => i.sprite != null).ToArray();
            Image named = images.FirstOrDefault(i => i.sprite.name.ToLowerInvariant().Contains("troph"));
            if (named != null)
                return named;
            return images.Where(i => i.gameObject != b.gameObject && i.type == Image.Type.Simple)
                       .OrderByDescending(i => i.rectTransform.rect.width * i.rectTransform.rect.height).FirstOrDefault()
                   ?? images.FirstOrDefault(i => i.gameObject == b.gameObject);
        }

        /// A wolf's head, white with soft shading and a dark outline like the game's own icons (the button tints it).
        private static Sprite Icon()
        {
            if (_icon != null)
                return _icon;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.LoadImage(Convert.FromBase64String(IconPng));
            _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _icon;
        }

        private const string IconPng = "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";

        // ---------- the window ----------

        private static void Open()
        {
            var gui = _guiInstance?.GetValue(null) as Component;
            if (gui == null)
                return;
            if (_panel == null || !_panel)
                Build(gui);
            if (_panel == null)
                return;
            _panel.transform.SetAsLastSibling();
            _panel.SetActive(true);
            List<Dictionary<string, object>> mods = Hub.Mods();
            if (_tab == null || !mods.Any(m => Hub.Get<string>(m, "id") == _tab))
                _tab = mods.Select(m => Hub.Get<string>(m, "id")).FirstOrDefault();
            Fill();
        }

        private static void Close()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }

        private static void Build(Component gui)
        {
            var trophies = _trophiesPanel.GetValue(gui) as GameObject;
            if (trophies == null)
            {
                Hub.Log?.LogWarning("Mod hub: the game's Trophies window was not found.");
                return;
            }
            FindTemplates(gui);
            _panel = Object.Instantiate(trophies, trophies.transform.parent);
            _panel.name = "WolvyreenModHubWindow";
            _panel.SetActive(false);
            StripGameScripts(_panel, keepTooltips: false);

            // the list: the window's scroll view, emptied
            ScrollRect scroll = _panel.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault();
            if (scroll != null && scroll.content != null)
            {
                _content = scroll.content;
                foreach (Transform t in _content.Cast<Transform>().ToList())
                    Object.DestroyImmediate(t.gameObject);
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.anchoredPosition = Vector2.zero;
                scroll.horizontal = false;
                scroll.vertical = true;
            }
            else
            {
                Hub.Log?.LogWarning("Mod hub: the Trophies window has no scroll view; using a plain list.");
                _content = NewRect(_panel.transform, "List");
                Stretch(_content, 30, 90, 30, 30);
            }
            var layout = _content.gameObject.GetComponent<VerticalLayoutGroup>() ?? _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;   // settings lines keep their width; a mod's own panels stretch themselves
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            layout.padding = new RectOffset(20, 24, 14, 20);
            var fit = _content.gameObject.GetComponent<ContentSizeFitter>() ?? _content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // the title: the biggest text outside the list
            TMP_Text title = _panel.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => !t.transform.IsChildOf(_content)).OrderByDescending(t => t.fontSize).FirstOrDefault();
            if (title != null)
            {
                title.text = Title;
                if (_headFont == null)
                    _headFont = title.font;
            }

            // close: the button that closed the Trophies window
            foreach (Button b in _panel.GetComponentsInChildren<Button>(true))
            {
                if (b.transform.IsChildOf(_content))
                    continue;
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(Close);
            }

            // tabs: a row above the list (the list is shortened to make room)
            Transform viewArea = scroll != null ? scroll.transform : _content;
            var view = (RectTransform)viewArea;
            _tabs = NewRect(view.parent, "Tabs");
            _tabs.anchorMin = new Vector2(view.anchorMin.x, view.anchorMax.y);
            _tabs.anchorMax = view.anchorMax;
            _tabs.pivot = new Vector2(0.5f, 1f);
            _tabs.offsetMin = new Vector2(view.offsetMin.x, view.offsetMax.y - 44f);
            _tabs.offsetMax = new Vector2(view.offsetMax.x, view.offsetMax.y);
            view.offsetMax = new Vector2(view.offsetMax.x, view.offsetMax.y - 50f);
            var row = _tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleLeft;

            // the mod's sections: a list on the left (the list view is moved right to make room)
            _side = NewRect(view.parent, "Sections");
            _side.anchorMin = view.anchorMin;
            _side.anchorMax = new Vector2(view.anchorMin.x, view.anchorMax.y);
            _side.pivot = new Vector2(0f, 1f);
            _side.offsetMin = view.offsetMin;
            _side.offsetMax = new Vector2(view.offsetMin.x + 240f, view.offsetMax.y);
            view.offsetMin = new Vector2(view.offsetMin.x + 252f, view.offsetMin.y);
            _side.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var sideList = _side.gameObject.AddComponent<VerticalLayoutGroup>();
            sideList.childControlWidth = true;
            sideList.childControlHeight = true;
            sideList.childForceExpandWidth = true;
            sideList.childForceExpandHeight = false;
            sideList.padding = new RectOffset(0, 0, 10, 10);
            sideList.spacing = 2f;
            var viewBack = view.GetComponent<Image>() ?? view.gameObject.AddComponent<Image>();
            if (viewBack.sprite == null)
                viewBack.color = new Color(0f, 0f, 0f, 0.3f);

            _holder = new GameObject("Templates");
            _holder.SetActive(false);
            _holder.transform.SetParent(_panel.transform, false);
            Report(gui, trophies, scroll, title);
        }

        /// Rebuilds the tabs and the current tab's page (values are read fresh every time).
        private static void Fill()
        {
            Fields.Clear();
            foreach (Transform t in _tabs.Cast<Transform>().ToList())
                Object.DestroyImmediate(t.gameObject);
            foreach (Transform t in _content.Cast<Transform>().ToList())
                Object.DestroyImmediate(t.gameObject);

            List<Dictionary<string, object>> mods = Hub.Mods();
            foreach (Dictionary<string, object> m in mods)
            {
                string id = Hub.Get<string>(m, "id");
                Button tab = TextButton(_tabs, Hub.Get<string>(m, "name"), _tabT, 18);
                Layout(tab.gameObject, 190f, 40f);
                tab.interactable = id != _tab;
                tab.onClick.AddListener(() => { _tab = id; Safe(Fill); });
            }

            foreach (Transform t in _side.Cast<Transform>().ToList())
                Object.DestroyImmediate(t.gameObject);
            Resets.Clear();
            Dictionary<string, object> mod = mods.FirstOrDefault(m => Hub.Get<string>(m, "id") == _tab);
            if (mod == null)
                return;
            var config = Hub.Get<ConfigFile>(mod, "config");
            List<Shown> shown = config != null ? Entries(config) : new List<Shown>();
            List<string> sections = shown.Select(x => x.Entry.Definition.Section).Distinct().ToList();
            if (!Sections.TryGetValue(_tab, out string section) || !sections.Contains(section))
                section = sections.FirstOrDefault();
            Sections[_tab] = section;
            foreach (string name in sections)
            {
                string chosen = name;
                HubKit.SideItem(_side, name, name == section, () => { Sections[_tab] = chosen; Safe(Fill); });
            }
            RectTransform gap = NewRect(_side, "Gap");
            Layout(gap.gameObject, -1f, 0f).flexibleHeight = 1f;
            TMP_Text version = Text(_side, $"  {Hub.Get<string>(mod, "name")} {Hub.Get<string>(mod, "version")}", 13, Muted, _bodyFont);
            version.textWrappingMode = TextWrappingModes.Normal;

            Hub.Get<Action<object>>(mod, "page")?.Invoke(_content);
            if (section != null)
            {
                TMP_Text head = Text(_content, section.ToUpperInvariant(), 26, Orange, _headFont ?? _bodyFont);
                Layout(head.gameObject, HubKit.SettingWidth, 40f);
                Settings(shown.Where(x => x.Entry.Definition.Section == section).ToList(), Hub.Get<Func<ConfigEntryBase, object, float>>(mod, "rows"));
            }
            if (config != null && Watched.Add(config))
                config.SettingChanged += (o, a) =>
                {
                    // a setting changed: its reset arrow shows only while it is not at its default
                    if (a?.ChangedSetting != null && Resets.TryGetValue(a.ChangedSetting, out GameObject arrow) && arrow != null)
                        arrow.SetActive(!Equals(a.ChangedSetting.BoxedValue, a.ChangedSetting.DefaultValue));
                };
            Canvas.ForceUpdateCanvases();
        }

        // ---------- settings ----------

        private sealed class Shown
        {
            public ConfigEntryBase Entry;
            public int Index, Order;
            public bool Browsable = true, ReadOnly, HideDefault, HideName, Custom;
            public string Name;
        }

        /// Every setting the window shows, in the order Configuration Manager would.
        private static List<Shown> Entries(ConfigFile config)
        {
            var all = new List<Shown>();
            int i = 0;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in config)
                all.Add(Read(kv.Value, i++));
            var sectionOrder = all.Select(x => x.Entry.Definition.Section).Distinct().ToList();
            return all.Where(x => x.Browsable)
                .OrderBy(x => sectionOrder.IndexOf(x.Entry.Definition.Section))
                .ThenByDescending(x => x.Order).ThenBy(x => x.Index).ToList();
        }

        private static void Settings(List<Shown> shown, Func<ConfigEntryBase, object, float> rows)
        {
            foreach (Shown s in shown)
            {
                try
                {
                    float used = rows != null ? rows(s.Entry, _content) : -1f;
                    if (used < 0f)
                        Row(s);
                }
                catch (Exception e)
                {
                    Hub.Log?.LogWarning($"Mod hub: the setting {s.Entry.Definition} could not be shown: {e.Message}");
                }
            }
        }

        /// What Configuration Manager's attributes (the mod's own copy, read by name) say about a setting.
        private static Shown Read(ConfigEntryBase e, int index)
        {
            string key = e.Definition.Key;
            var s = new Shown { Entry = e, Index = index, Name = key.IndexOf(' ') < 0 ? Nice(key) : key };   // "SightingRange" -> "Sighting Range"
            object attrs = e.Description?.Tags?.FirstOrDefault(t => t != null && t.GetType().Name == "ConfigurationManagerAttributes");
            if (attrs == null)
                return s;
            if (Attr(attrs, "Browsable") is bool b)
                s.Browsable = b;
            if (Attr(attrs, "Order") is int o)
                s.Order = o;
            if (Attr(attrs, "ReadOnly") is bool r)
                s.ReadOnly = r;
            if (Attr(attrs, "HideDefaultButton") is bool h)
                s.HideDefault = h;
            if (Attr(attrs, "HideSettingName") is bool hn)
                s.HideName = hn;
            if (Attr(attrs, "DispName") is string dn && dn.Length > 0)
                s.Name = dn;
            s.Custom = Attr(attrs, "CustomDrawer") != null;
            return s;
        }

        private static object Attr(object attrs, string name)
        {
            FieldInfo f = attrs.GetType().GetField(name);
            if (f != null)
                return f.GetValue(attrs);
            PropertyInfo p = attrs.GetType().GetProperty(name);
            return p != null ? p.GetValue(attrs, null) : null;
        }

        private static void Row(Shown s)
        {
            ConfigEntryBase e = s.Entry;
            Type type = e.SettingType;
            bool simple = type == typeof(bool) || type.IsEnum || type == typeof(int) || type == typeof(float) || type == typeof(double)
                          || type == typeof(Color) || type == typeof(KeyboardShortcut);
            if (s.Custom && !simple)
            {
                // drawn by the mod itself in Configuration Manager; not in this window yet
                RectTransform note = HubKit.SettingRow(_content, s.HideName ? "" : s.Name, e.Description?.Description);
                Text(note, "(not in this window yet)", 15, Muted, _bodyFont);
                return;
            }

            RectTransform row = HubKit.SettingRow(_content, s.Name, e.Description?.Description);
            GameObject control;
            if (type == typeof(bool))
                control = ToggleFor(row, e);
            else if (type.IsEnum)
                control = DropdownFor(row, e, Enum.GetNames(type).ToList(), Nice);
            else if (Range(e, out float min, out float max) && (type == typeof(int) || type == typeof(float) || type == typeof(double)))
                control = SliderFor(row, e, min, max, type == typeof(int));
            else if (List(e, out List<string> values))
                control = DropdownFor(row, e, values, v => v);
            else if (type == typeof(Color))
                control = HubKit.ColourControl(row, _content, (Color)e.BoxedValue, c => e.BoxedValue = c);
            else
                control = InputFor(row, e);
            if (control != null)
            {
                foreach (Selectable sel in control.GetComponentsInChildren<Selectable>(true))
                    sel.interactable = !s.ReadOnly;
            }
            // reset: an arrow, only while the setting is not at its default
            if (!s.HideDefault && !s.ReadOnly)
            {
                Button reset = HubKit.IconButton(row, HubKit.ResetIcon(), () =>
                {
                    e.BoxedValue = e.DefaultValue;
                    Safe(Fill);
                });
                HubKit.Tooltip(reset.gameObject, "Reset", "Back to the default: " + Show(e.DefaultValue));
                reset.gameObject.SetActive(!Equals(e.BoxedValue, e.DefaultValue));
                Resets[e] = reset.gameObject;
            }
            else
                HubKit.Space(row, 28f);
        }

        private static RectTransform RowRect(string label, string description)
        {
            RectTransform row = NewRect(_content, "Row");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 10f;
            Layout(row.gameObject, -1f, 34f);
            TMP_Text name = Text(row, label, 17, Beige, _bodyFont);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var le = Layout(name.gameObject, 260f, 30f);
            le.flexibleWidth = 1f;
            if (!string.IsNullOrEmpty(description))
            {
                name.raycastTarget = true;
                SetTooltip(name.gameObject, label, description);
            }
            return row;
        }

        private static GameObject ToggleFor(RectTransform row, ConfigEntryBase e)
        {
            return HubKit.Switch(row, (bool)e.BoxedValue, v => e.BoxedValue = v);
        }

        private static GameObject SliderFor(RectTransform row, ConfigEntryBase e, float min, float max, bool whole)
        {
            if (_sliderT == null)
                return InputFor(row, e);
            Slider s = Copy(_sliderT, row);
            foreach (TMP_Text label in s.GetComponentsInChildren<TMP_Text>(true))
                label.text = "";
            s.onValueChanged = new Slider.SliderEvent();
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.value = Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture);
            Layout(s.gameObject, 200f, 24f);
            TMP_Text value = Text(row, Show(e.BoxedValue), 16, Beige, _bodyFont);
            Layout(value.gameObject, 50f, 30f);
            s.onValueChanged.AddListener(v =>
            {
                e.BoxedValue = e.SettingType == typeof(int) ? (object)Mathf.RoundToInt(v)
                    : e.SettingType == typeof(double) ? (object)(double)v : (object)v;
                value.text = Show(e.BoxedValue);
            });
            return s.gameObject;
        }

        private static GameObject DropdownFor(RectTransform row, ConfigEntryBase e, List<string> values, Func<string, string> nice)
        {
            string current = e.SettingType.IsEnum ? e.BoxedValue.ToString() : TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType);
            if (_dropT == null)
            {
                // plain fallback: a button that steps through the values
                Button b = TextButton(row, nice(current), _tabT, 15);
                Layout(b.gameObject, 260f, 30f);
                b.onClick.AddListener(() =>
                {
                    int at = values.IndexOf(current);
                    current = values[(at + 1) % values.Count];
                    e.BoxedValue = TomlTypeConverter.ConvertToValue(current, e.SettingType);
                    b.GetComponentInChildren<TMP_Text>(true).text = nice(current);
                });
                return b.gameObject;
            }
            TMP_Dropdown d = HubKit.CleanDropdown(Copy(_dropT, row));
            d.onValueChanged = new TMP_Dropdown.DropdownEvent();
            d.ClearOptions();
            d.AddOptions(values.Select(nice).ToList());
            d.SetValueWithoutNotify(Math.Max(0, values.IndexOf(current)));
            d.RefreshShownValue();
            d.onValueChanged.AddListener(i => e.BoxedValue = TomlTypeConverter.ConvertToValue(values[i], e.SettingType));
            Layout(d.gameObject, 240f, 30f);
            return d.gameObject;
        }

        private static GameObject InputFor(RectTransform row, ConfigEntryBase e)
        {
            string text = TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType);
            if (e.SettingType == typeof(Color))
            {
                RectTransform sw = NewRect(row, "Swatch");
                var img = sw.gameObject.AddComponent<Image>();
                img.color = (Color)e.BoxedValue;
                Layout(sw.gameObject, 30f, 24f);
            }
            if (_inputT == null)
            {
                TMP_Text t = Text(row, text, 16, Beige, _bodyFont);
                Layout(t.gameObject, 260f, 30f);
                return t.gameObject;
            }
            TMP_InputField f = Copy(_inputT, row);
            f.onValueChanged = new TMP_InputField.OnChangeEvent();
            f.onEndEdit = new TMP_InputField.SubmitEvent();
            f.onSubmit = new TMP_InputField.SubmitEvent();
            f.onSelect = new TMP_InputField.SelectionEvent();
            f.onDeselect = new TMP_InputField.SelectionEvent();
            f.characterLimit = 0;
            f.contentType = TMP_InputField.ContentType.Standard;
            f.lineType = TMP_InputField.LineType.SingleLine;
            if (f.placeholder is TMP_Text ph)
                ph.text = "";
            f.SetTextWithoutNotify(text);
            f.onEndEdit.AddListener(v =>
            {
                try
                {
                    e.BoxedValue = TomlTypeConverter.ConvertToValue(v, e.SettingType);
                }
                catch
                {
                    // not a valid value: keep the old one
                }
                f.SetTextWithoutNotify(TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType));
                if (e.SettingType == typeof(Color))
                    Safe(Fill);
            });
            Fields.Add(f);
            Layout(f.gameObject, 260f, 30f);
            return f.gameObject;
        }

        private static bool Range(ConfigEntryBase e, out float min, out float max)
        {
            min = max = 0f;
            object r = e.Description?.AcceptableValues;
            if (r == null || !r.GetType().Name.StartsWith("AcceptableValueRange", StringComparison.Ordinal))
                return false;
            min = Convert.ToSingle(r.GetType().GetProperty("MinValue")?.GetValue(r, null), CultureInfo.InvariantCulture);
            max = Convert.ToSingle(r.GetType().GetProperty("MaxValue")?.GetValue(r, null), CultureInfo.InvariantCulture);
            return max > min;
        }

        private static bool List(ConfigEntryBase e, out List<string> values)
        {
            values = null;
            object r = e.Description?.AcceptableValues;
            if (r == null || !r.GetType().Name.StartsWith("AcceptableValueList", StringComparison.Ordinal))
                return false;
            var arr = r.GetType().GetProperty("AcceptableValues")?.GetValue(r, null) as Array;
            if (arr == null || arr.Length == 0)
                return false;
            values = arr.Cast<object>().Select(v => TomlTypeConverter.ConvertToString(v, e.SettingType)).ToList();
            return true;
        }

        private static string Show(object v)
        {
            if (v is float f)
                return f.ToString("0.##", CultureInfo.InvariantCulture);
            if (v is double d)
                return d.ToString("0.##", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// "OneStarAndUp" -> "One Star And Up"
        internal static string Nice(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(name[i - 1]))
                    sb.Append(' ');
                sb.Append(c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }

        // ---------- the game's parts ----------

        internal static void FindTemplates(Component gui)
        {
            object menu = _menuInstance?.GetValue(null);
            var settings = menu != null ? _settingsPrefab?.GetValue(menu) as GameObject : null;
            if (settings != null)
            {
                _toggleT = settings.GetComponentsInChildren<Toggle>(true).FirstOrDefault();
                _sliderT = settings.GetComponentsInChildren<Slider>(true).FirstOrDefault();
                _dropT = settings.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault();
                _inputT = settings.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault();
                _textButtonT = settings.GetComponentsInChildren<Button>(true)
                    .FirstOrDefault(b => b.GetComponentInChildren<TMP_Text>(true) != null && b.GetComponent<Toggle>() == null);
            }
            if (_inputT == null)
                _inputT = Resources.FindObjectsOfTypeAll<TMP_InputField>()
                    .Where(f => f != null && f.gameObject.scene.IsValid() && f.textComponent != null && f.GetComponent<Image>() != null)
                    .OrderBy(f => f.gameObject.name.IndexOf("chat", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                    .FirstOrDefault();
            _tabT = _tabCraft?.GetValue(gui) as Button;
            if (_textButtonT == null)
                _textButtonT = _tabT;

            // fonts: the game's body text (Averia) and headings (Norse)
            foreach (TMP_Text t in gui.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == null)
                    continue;
                string n = t.font.name.ToLowerInvariant();
                if (_bodyFont == null && n.Contains("averia"))
                    _bodyFont = t.font;
                if (_headFont == null && n.Contains("norse"))
                    _headFont = t.font;
            }

            // a tooltip prefab from the inventory's own buttons
            if (_tooltipType != null && _tipPrefab != null)
                foreach (Component c in gui.GetComponentsInChildren(_tooltipType, true))
                {
                    _tipPrefabObj = _tipPrefab.GetValue(c) as GameObject;
                    if (_tipPrefabObj != null)
                        break;
                }
        }

        /// A copy of a game control, made in an inactive holder so none of its scripts run before the
        /// game-only ones are taken off.
        internal static T Copy<T>(T template, Transform parent) where T : Component
        {
            GameObject go = Object.Instantiate(template.gameObject, Holder());
            go.name = template.gameObject.name;
            StripGameScripts(go, keepTooltips: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            return go.GetComponent<T>();
        }

        internal static Button TextButton(Transform parent, string text, Button template, int size)
        {
            Button b;
            if (template != null)
            {
                b = Copy(template, parent);
                b.onClick = new Button.ButtonClickedEvent();
                b.interactable = true;
                TMP_Text[] texts = b.GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length > 0)
                {
                    texts[0].text = text;
                    for (int i = 1; i < texts.Length; i++)
                        texts[i].text = "";
                }
                else
                    Text(b.transform, text, size, Beige, _bodyFont).alignment = TextAlignmentOptions.Center;
            }
            else
            {
                RectTransform rt = NewRect(parent, "Button");
                rt.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.2f, 0.15f, 0.9f);
                b = rt.gameObject.AddComponent<Button>();
                TMP_Text t = Text(rt, text, size, Beige, _bodyFont);
                t.alignment = TextAlignmentOptions.Center;
                Stretch((RectTransform)t.transform, 0, 0, 0, 0);
            }
            return b;
        }

        /// Takes off the game's own scripts (translation, gamepad hints, ...), keeping Unity's UI parts
        /// and the game's click sounds.
        private static void StripGameScripts(GameObject go, bool keepTooltips)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                Type t = mb.GetType();
                string ns = t.Namespace ?? "";
                if (ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal))
                    continue;
                // the game's own versions of Unity's controls (GuiSlider, ...) and their sounds stay
                if (mb is UIBehaviour || t.Name.EndsWith("Sfx", StringComparison.Ordinal) || (keepTooltips && t == _tooltipType))
                    continue;
                Object.DestroyImmediate(mb);
            }
            foreach (Transform hint in go.GetComponentsInChildren<Transform>(true)
                         .Where(x => x != go.transform && x.name.IndexOf("gamepad", StringComparison.OrdinalIgnoreCase) >= 0).ToList())
                if (hint != null)
                    Object.DestroyImmediate(hint.gameObject);
        }

        internal static void SetTooltip(GameObject go, string topic, string text)
        {
            if (_tooltipType == null)
                return;
            Component tip = go.GetComponent(_tooltipType) ?? go.GetComponentInChildren(_tooltipType, true);
            if (tip == null)
            {
                if (_tipPrefabObj == null)
                    return;
                tip = go.AddComponent(_tooltipType);
                _tipPrefab.SetValue(tip, _tipPrefabObj);
            }
            _tipTopic?.SetValue(tip, topic);
            _tipText?.SetValue(tip, text);
        }

        internal static TMP_Text Text(Transform parent, string text, int size, Color colour, TMP_FontAsset font)
        {
            RectTransform rt = NewRect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                t.font = font;
            t.fontSize = size;
            t.color = colour;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            Layout(rt.gameObject, -1f, size + 10f);
            return t;
        }

        internal static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        internal static LayoutElement Layout(GameObject go, float width, float height)
        {
            LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0f)
            {
                le.preferredWidth = width;
                le.minWidth = Mathf.Min(width, 40f);
            }
            if (height >= 0f)
            {
                le.preferredHeight = height;
                le.minHeight = height;
            }
            return le;
        }

        private static string PathTo(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (Transform x = t; x != null && x != root; x = x.parent)
                parts.Insert(0, x.name);
            return string.Join("/", parts.ToArray());
        }

        /// Once per game: what was found, so a game update can be fixed quickly.
        private static void Report(Component gui, GameObject trophies, ScrollRect scroll, TMP_Text title)
        {
            if (_reported)
                return;
            _reported = true;
            var sb = new StringBuilder("Mod hub parts: ");
            sb.Append($"window={trophies.name} ({((RectTransform)trophies.transform).rect.size}), scroll={(scroll != null ? PathTo(scroll.transform, _panel.transform) : "none")}, ");
            sb.Append($"title={(title != null ? PathTo(title.transform, _panel.transform) : "none")}, ");
            sb.Append($"toggle={Name(_toggleT)}, slider={Name(_sliderT)}, dropdown={Name(_dropT)}, input={Name(_inputT)}, button={Name(_textButtonT)}, tab={Name(_tabT)}, ");
            sb.Append($"fonts={(_bodyFont != null ? _bodyFont.name : "none")}/{(_headFont != null ? _headFont.name : "none")}, tooltip={(_tipPrefabObj != null ? _tipPrefabObj.name : "none")}");
            Hub.Log?.LogInfo(sb.ToString());
            Hub.Log?.LogInfo("Mod hub window parts: " + Tree(_panel.transform, 0));
        }

        private static string Name(Component c) => c != null ? $"{c.gameObject.name} ({((RectTransform)c.transform).rect.size})" : "none";

        private static string Tree(Transform t, int depth)
        {
            if (depth > 4)
                return "";
            var parts = new List<string>();
            foreach (Transform c in t)
                parts.Add(c.name + (c.childCount > 0 && depth < 4 ? "[" + Tree(c, depth + 1) + "]" : ""));
            return string.Join(", ", parts.ToArray());
        }

        private static void Remove()
        {
            foreach (KeyValuePair<RectTransform, Vector2> kv in Moved)
                if (kv.Key != null)
                    kv.Key.anchoredPosition = kv.Value;
            Moved.Clear();
            if (_legacy != null && _legacy)
                _legacy.SetActive(true);   // Drop Counters keeps a way in when this window goes
            _legacy = null;
            if (_button != null)
                Object.Destroy(_button);
            if (_panel != null)
                Object.Destroy(_panel);
            _button = _panel = _holder = null;
            _content = _tabs = null;
            Fields.Clear();
        }

        internal static void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                if (!_failedOnce)
                    Hub.Log?.LogWarning($"Mod hub: something went wrong (reported once): {e}");
                _failedOnce = true;
            }
        }
    }
}
