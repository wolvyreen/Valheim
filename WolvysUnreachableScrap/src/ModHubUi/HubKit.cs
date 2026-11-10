// Wolvyreen Mod Hub - game-styled building blocks (shared: the same file, unchanged, goes into every one of Wolvyreen's mods)
//
// For a mod's own editors in the hub window (the "rows" it gives Hub.Register): text, buttons, check boxes,
// sliders, drop-downs, text boxes, picture tiles, a colour picker, and rows / columns / dark boxes to lay them
// out. Every control is a copy of the game's own (see HubWindow), so a mod's editors look like the game.
// A panel that changes (something opens, a Find box narrows a list) clears and rebuilds only its own part.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WolvyreenModHub
{
    internal static class HubKit
    {
        internal static readonly Color Beige = HubWindow.Beige;
        internal static readonly Color Muted = HubWindow.Muted;
        internal static readonly Color Orange = HubWindow.Orange;
        internal static readonly Color Gold = new Color(1f, 0.85f, 0.36f, 1f);
        internal static readonly Color TileColour = new Color(0.08f, 0.07f, 0.06f, 0.95f);
        internal static readonly Color BoxColour = new Color(0f, 0f, 0f, 0.28f);

        internal static bool Ready() => HubWindow.Ready();

        // ---------- layout ----------

        /// A column: children one under the other, full width, as tall as they need.
        internal static RectTransform Column(Transform parent, float spacing = 4f, int pad = 0)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Column");
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            return rt;
        }

        /// A row: children side by side, left to right, centred vertically.
        internal static RectTransform Row(Transform parent, float spacing = 8f, float height = 34f)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Row");
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = spacing;
            if (height > 0f)
                HubWindow.Layout(rt.gameObject, -1f, height);
            return rt;
        }

        /// A dark box (for something opened under a row), a column inside.
        internal static RectTransform Box(Transform parent, int indent = 20, bool fill = true)
        {
            RectTransform outer = Column(parent, 0f);
            var ov = (VerticalLayoutGroup)outer.GetComponent<LayoutGroup>();
            ov.padding = new RectOffset(indent, 0, 0, 0);
            ov.childForceExpandWidth = fill;   // not filling: the box is as wide as what is in it
            RectTransform box = Column(outer, 6f, 10);
            box.gameObject.AddComponent<Image>().color = BoxColour;
            if (!fill)
                ((VerticalLayoutGroup)box.GetComponent<LayoutGroup>()).childForceExpandWidth = false;
            return box;
        }

        /// A grid of fixed-size cells, as many columns as fit.
        internal static RectTransform Grid(Transform parent, float cellW, float cellH, float gap = 4f)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Grid");
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(cellW, cellH);
            g.spacing = new Vector2(gap, gap);
            g.startCorner = GridLayoutGroup.Corner.UpperLeft;
            g.startAxis = GridLayoutGroup.Axis.Horizontal;
            g.childAlignment = TextAnchor.UpperLeft;
            g.constraint = GridLayoutGroup.Constraint.Flexible;
            return rt;
        }

        internal static void Space(Transform parent, float width) => HubWindow.Layout(HubWindow.NewRect(parent, "Space").gameObject, width, 1f);

        internal static void Clear(Transform t)
        {
            foreach (Transform c in t.Cast<Transform>().ToList())
                Object.DestroyImmediate(c.gameObject);
            HubWindow.Fields.RemoveAll(f => f == null);
        }

        /// Fixed size (a negative number leaves that side alone); flexible > 0 lets it take the free width.
        internal static T Size<T>(T c, float width, float height = -1f, float flexible = 0f) where T : Component
        {
            LayoutElement le = HubWindow.Layout(c.gameObject, width, height);
            if (flexible > 0f)
                le.flexibleWidth = flexible;
            return c;
        }

        // ---------- controls ----------

        internal static TMP_Text Text(Transform parent, string text, int size = 17, Color? colour = null, bool heading = false, float width = -1f)
        {
            TMP_Text t = HubWindow.Text(parent, text, size, colour ?? Beige, heading ? (HubWindow._headFont ?? HubWindow._bodyFont) : HubWindow._bodyFont);
            if (width >= 0f)
                HubWindow.Layout(t.gameObject, width, -1f);
            return t;
        }

        internal static Button Button(Transform parent, string text, Action click, float width = 160f, float height = 30f, int size = 15)
        {
            Button b = HubWindow.TextButton(parent, text, HubWindow._tabT, size);
            // one line, shrunk to fit if it must (never wrapping over the button's edge)
            foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true))
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.enableAutoSizing = true;
                t.fontSizeMax = size;
                t.fontSizeMin = Math.Min(size, 11);
                t.margin = new Vector4(6f, 0f, 6f, 0f);
            }
            HubWindow.Layout(b.gameObject, width, height);
            if (click != null)
                b.onClick.AddListener(() => HubWindow.Safe(click));
            return b;
        }

        internal static void SetText(Button b, string text)
        {
            TMP_Text t = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
            if (t != null)
                t.text = text;
        }

        internal static Toggle Toggle(Transform parent, bool on, Action<bool> changed)
        {
            if (HubWindow._toggleT == null)
            {
                Button b = null;
                b = Button(parent, on ? "On" : "Off", () =>
                {
                    on = !on;
                    SetText(b, on ? "On" : "Off");
                    changed(on);
                }, 70f);
                return null;
            }
            Toggle t = HubWindow.Copy(HubWindow._toggleT, parent);
            foreach (TMP_Text label in t.GetComponentsInChildren<TMP_Text>(true))
                label.gameObject.SetActive(false);
            // Only the check box itself may take the click: in the game's Settings menu the whole line (label and
            // all) is clickable, and copied here that area covered the row, so any click on the row ticked it.
            foreach (Graphic g in t.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
            Graphic hit = t.GetComponent<Graphic>();
            if (hit == null)
            {
                var img = t.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                hit = img;
            }
            hit.raycastTarget = true;
            foreach (RectTransform child in t.transform.Cast<Transform>().OfType<RectTransform>())
            {
                // keep each part inside the box's own cell
                if (child.anchorMin != child.anchorMax)
                {
                    child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
                    child.anchoredPosition = Vector2.zero;
                    child.sizeDelta = new Vector2(Mathf.Min(child.rect.width, 24f), Mathf.Min(child.rect.height, 24f));
                }
            }
            t.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
            t.group = null;
            t.isOn = on;
            t.onValueChanged.AddListener(v => HubWindow.Safe(() => changed(v)));
            HubWindow.Layout(t.gameObject, 40f, 30f);
            return t;
        }

        /// A slider with its value written after it.
        internal static Slider Slider(Transform parent, float min, float max, bool whole, float value, Action<float> changed, float width = 220f,
            Func<float, string> show = null)
        {
            show = show ?? (v => whole ? Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture));
            if (HubWindow._sliderT == null)
                return null;
            Slider s = HubWindow.Copy(HubWindow._sliderT, parent);
            foreach (TMP_Text label in s.GetComponentsInChildren<TMP_Text>(true))
                label.text = "";
            OnlyBarTakesClicks(s);
            s.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.value = value;
            HubWindow.Layout(s.gameObject, width, 24f);
            TMP_Text text = Text(parent, show(value), 16, Beige, false, 60f);
            s.onValueChanged.AddListener(v =>
            {
                text.text = show(v);
                HubWindow.Safe(() => changed(v));
            });
            return s;
        }

        /// (hub 11) Only the slider's own bar may take a click. The game's Settings slider carries its (emptied) label
        /// beside the bar, and that label still caught clicks: in the colour picker it lay over the wheel, so a click
        /// on the wheel moved Brightness or Opacity instead of picking the colour.
        internal static void OnlyBarTakesClicks(Slider s)
        {
            foreach (Graphic g in s.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
            Graphic hit = s.GetComponent<Graphic>();
            if (hit == null)
            {
                var img = s.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                hit = img;
            }
            hit.raycastTarget = true;
        }

        internal static TMP_Dropdown Dropdown(Transform parent, List<string> options, int index, Action<int> changed, float width = 260f)
        {
            if (HubWindow._dropT == null)
            {
                Button b = null;
                b = Button(parent, options[Math.Max(0, index)], () =>
                {
                    index = (index + 1) % options.Count;
                    SetText(b, options[index]);
                    changed(index);
                }, width);
                return null;
            }
            TMP_Dropdown d = CleanDropdown(HubWindow.Copy(HubWindow._dropT, parent));
            d.onValueChanged = new TMP_Dropdown.DropdownEvent();
            d.ClearOptions();
            d.AddOptions(options);
            d.SetValueWithoutNotify(Math.Max(0, index));
            d.RefreshShownValue();
            d.onValueChanged.AddListener(i => HubWindow.Safe(() => changed(i)));
            HubWindow.Layout(d.gameObject, width, 30f);
            return d;
        }

        /// The game's drop-down comes with its Settings label beside it; only the box stays, in readable text.
        private static bool _dropLogged;

        internal static TMP_Dropdown CleanDropdown(TMP_Dropdown d)
        {
            foreach (TMP_Text t in d.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == d.captionText || t == d.itemText)
                {
                    // (hub 12/13) the game's drop-down text sizes itself to fit a box shorter than its line, so in ours
                    // it came out tiny (hub 12's fixed size with Ellipsis then showed nothing): a fixed size, on one
                    // line, always drawn, centred on the box's middle
                    if (!_dropLogged)
                    {
                        _dropLogged = true;
                        Hub.Log?.LogInfo($"Mod hub drop-down text: '{t.name}' box {t.rectTransform.rect.size}, autosize {t.enableAutoSizing} " +
                                         $"({t.fontSizeMin}-{t.fontSizeMax}), overflow {t.overflowMode}, parent '{t.transform.parent?.name}'.");
                    }
                    t.enableAutoSizing = false;
                    t.fontSize = 16;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.alignment = t == d.captionText ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
                    if (t == d.captionText)
                    {
                        RectTransform r = t.rectTransform;   // the whole box, less room for the arrow
                        r.SetParent(d.transform, false);
                        r.anchorMin = Vector2.zero;
                        r.anchorMax = Vector2.one;
                        r.offsetMin = new Vector2(10f, 0f);
                        r.offsetMax = new Vector2(-10f, 0f);
                    }
                }
                else if (d.template == null || !t.transform.IsChildOf(d.template))
                    t.gameObject.SetActive(false);
            }
            return d;
        }

        /// A text box. changed: every key (a Find box); done: when the box is left or Enter is pressed.
        internal static TMP_InputField Input(Transform parent, string text, Action<string> changed, Action<string> done = null, float width = 260f,
            string placeholder = "")
        {
            if (HubWindow._inputT == null)
                return null;
            TMP_InputField f = HubWindow.Copy(HubWindow._inputT, parent);
            f.onValueChanged = new TMP_InputField.OnChangeEvent();
            f.onEndEdit = new TMP_InputField.SubmitEvent();
            f.onSubmit = new TMP_InputField.SubmitEvent();
            f.onSelect = new TMP_InputField.SelectionEvent();
            f.onDeselect = new TMP_InputField.SelectionEvent();
            f.characterLimit = 0;
            f.contentType = TMP_InputField.ContentType.Standard;
            f.lineType = TMP_InputField.LineType.SingleLine;
            if (f.placeholder is TMP_Text ph)
                ph.text = placeholder;
            f.SetTextWithoutNotify(text ?? "");
            if (changed != null)
                f.onValueChanged.AddListener(v => HubWindow.Safe(() => changed(v)));
            if (done != null)
                f.onEndEdit.AddListener(v => HubWindow.Safe(() => done(v)));
            HubWindow.Fields.Add(f);
            HubWindow.Layout(f.gameObject, width, 30f);
            return f;
        }

        /// A picture on a dark tile; gold frame when chosen; click to pick. tip: the name shown on hover.
        internal static Button Tile(Transform parent, Sprite sprite, bool chosen, Action click, float size = 40f, string tip = null)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Tile");
            HubWindow.Layout(rt.gameObject, size, size);
            var frame = rt.gameObject.AddComponent<Image>();
            frame.color = chosen ? Gold : TileColour;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = frame;
            RectTransform inner = HubWindow.NewRect(rt, "Back");
            HubWindow.Stretch(inner, 2, 2, 2, 2);
            inner.gameObject.AddComponent<Image>().color = TileColour;
            inner.GetComponent<Image>().raycastTarget = false;
            if (sprite != null)
            {
                RectTransform pic = HubWindow.NewRect(rt, "Picture");
                HubWindow.Stretch(pic, 4, 4, 4, 4);
                var img = pic.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            if (click != null)
                b.onClick.AddListener(() => HubWindow.Safe(click));
            if (!string.IsNullOrEmpty(tip))
                HubWindow.SetTooltip(rt.gameObject, tip, "");
            return b;
        }

        /// A flat colour square.
        internal static Image Swatch(Transform parent, Color c, float w = 30f, float h = 24f)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Swatch");
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(c.r, c.g, c.b, 1f);
            HubWindow.Layout(rt.gameObject, w, h);
            return img;
        }

        internal static void Tooltip(GameObject go, string topic, string text) => HubWindow.SetTooltip(go, topic, text);

        // ---------- the window's own look (switch, choices, section list, setting lines) ----------

        internal static readonly Color Highlight = new Color(1f, 0.67f, 0.24f, 0.13f);
        internal static readonly Color Line = new Color(1f, 1f, 1f, 0.06f);

        /// An on/off switch (a pill with a knob), in the game's orange.
        internal static GameObject Switch(Transform parent, bool on, Action<bool> changed)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Switch");
            HubWindow.Layout(rt.gameObject, 50f, 26f);
            var back = rt.gameObject.AddComponent<Image>();
            back.sprite = Shape("pill", 50, 26, PillAlpha);
            RectTransform knob = HubWindow.NewRect(rt, "Knob");
            knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
            knob.sizeDelta = new Vector2(20f, 20f);
            var k = knob.gameObject.AddComponent<Image>();
            k.sprite = Shape("dot", 20, 20, (x, y) => Disc(x, y, 0.5f, 0.5f, 0.48f));
            k.raycastTarget = false;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = back;
            Action show = () =>
            {
                back.color = on ? new Color(0.62f, 0.38f, 0.1f, 1f) : new Color(0.24f, 0.2f, 0.17f, 1f);
                k.color = on ? new Color(1f, 0.81f, 0.5f, 1f) : new Color(0.6f, 0.54f, 0.44f, 1f);
                knob.anchoredPosition = new Vector2(on ? 38f : 12f, 0f);
            };
            show();
            b.onClick.AddListener(() =>
            {
                on = !on;
                show();
                HubWindow.Safe(() => changed(on));
            });
            return rt.gameObject;
        }

        /// A few choices side by side; the chosen one shows as selected (like the game's tabs).
        internal static RectTransform Choices(Transform parent, List<string> options, int index, Action<int> changed, float width = 78f)
        {
            RectTransform row = Row(parent, 4f, 32f);
            var buttons = new List<Button>();
            for (int i = 0; i < options.Count; i++)
            {
                int at = i;
                Button b = Button(row, options[i], null, width, 30f, 14);
                b.onClick.AddListener(() =>
                {
                    for (int j = 0; j < buttons.Count; j++)
                        buttons[j].interactable = j != at;
                    HubWindow.Safe(() => changed(at));
                });
                buttons.Add(b);
            }
            for (int j = 0; j < buttons.Count; j++)
                buttons[j].interactable = j != index;
            return row;
        }

        /// One entry of a list on the left (sections): highlighted with an orange bar when chosen.
        internal static Button SideItem(Transform parent, string text, bool chosen, Action click)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Section");
            HubWindow.Layout(rt.gameObject, -1f, 40f);
            var back = rt.gameObject.AddComponent<Image>();
            back.color = chosen ? Highlight : new Color(0f, 0f, 0f, 0f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = back;
            var cb = b.colors;
            cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            b.colors = cb;
            if (chosen)
            {
                RectTransform bar = HubWindow.NewRect(rt, "Bar");
                bar.anchorMin = new Vector2(0f, 0f);
                bar.anchorMax = new Vector2(0f, 1f);
                bar.pivot = new Vector2(0f, 0.5f);
                bar.sizeDelta = new Vector2(3f, 0f);
                bar.anchoredPosition = Vector2.zero;
                var bi = bar.gameObject.AddComponent<Image>();
                bi.color = Orange;
                bi.raycastTarget = false;
            }
            TMP_Text t = HubWindow.Text(rt, text, 17, chosen ? new Color(1f, 0.81f, 0.5f, 1f) : new Color(0.8f, 0.73f, 0.6f, 1f), HubWindow._bodyFont);
            HubWindow.Stretch((RectTransform)t.transform, 18, 0, 8, 0);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            Object.DestroyImmediate(t.GetComponent<LayoutElement>());
            if (click != null)
                b.onClick.AddListener(() => HubWindow.Safe(click));
            return b;
        }

        internal const float SettingWidth = 940f, ControlsWidth = 420f;
        /// (hub 10) The width of a setting's control: text box, drop-down and slider bar alike (a slider's number sits left of it).
        internal const float ControlWidth = 260f;

        /// One setting: its name with a short description under it on the left, its controls on the right (returned),
        /// then a thin line. The full description shows on hover.
        internal static RectTransform SettingRow(Transform parent, string name, string description)
        {
            RectTransform row = Row(parent, 12f, -1f);
            // (hub 10) at least SettingWidth wide, and as wide as the window's list: the row and the line under it both
            // stretch, so a bigger window gives the name and description more room and the controls stay at the end of
            // the line (hub 8 had kept both at SettingWidth)
            LayoutElement rowSize = HubWindow.Layout(row.gameObject, SettingWidth, 54f);
            rowSize.flexibleWidth = 1f;
            rowSize.preferredHeight = -1f;   // (hub 13) at least 54 high, taller when the description takes two lines
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 8, 6, 9);
            RectTransform left = Column(row, 2f);
            Size(left, 300f, -1f, 1f);
            TMP_Text n = Text(left, name, 17);
            n.textWrappingMode = TextWrappingModes.NoWrap;
            n.overflowMode = TextOverflowModes.Ellipsis;
            string first = FirstSentence(description);
            if (!string.IsNullOrEmpty(first))
            {
                TMP_Text d = Text(left, first, 13, Muted);
                d.textWrappingMode = TextWrappingModes.Normal;
            }
            if (!string.IsNullOrEmpty(description))
            {
                n.raycastTarget = true;
                Tooltip(n.gameObject, name, description);
            }
            RectTransform right = Row(row, 8f, -1f);
            right.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
            HubWindow.Layout(right.gameObject, ControlsWidth, 40f);
            RectTransform line = HubWindow.NewRect(parent, "Line");
            HubWindow.Layout(line.gameObject, SettingWidth, 1f).flexibleWidth = 1f;
            line.gameObject.AddComponent<Image>().color = Line;
            return right;
        }

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            int at = text.IndexOf(". ", StringComparison.Ordinal);
            string first = at > 0 ? text.Substring(0, at + 1) : text;
            return first.Length > 110 ? first.Substring(0, 107) + "..." : first;
        }

        /// A small picture button (no frame), e.g. the reset arrow.
        internal static Button IconButton(Transform parent, Sprite sprite, Action click, float size = 22f, Color? colour = null)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Icon");
            HubWindow.Layout(rt.gameObject, size + 6f, size + 6f);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform pic = HubWindow.NewRect(rt, "Picture");
            HubWindow.Stretch(pic, 3, 3, 3, 3);
            var img = pic.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = colour ?? new Color(0.8f, 0.7f, 0.5f, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(1.3f, 1.2f, 1f, 1f);
            b.colors = cb;
            if (click != null)
                b.onClick.AddListener(() => HubWindow.Safe(click));
            return b;
        }

        // ---------- small pictures, drawn once ----------

        private static readonly Dictionary<string, Sprite> Shapes = new Dictionary<string, Sprite>();

        /// A white picture from an alpha function over 0..1 x 0..1 (y up), 4x supersampled.
        internal static Sprite Shape(string key, int w, int h, Func<float, float, float> alpha)
        {
            if (Shapes.TryGetValue(key, out Sprite sp) && sp != null)
                return sp;
            int sw = w * 2, sh = h * 2;   // drawn at twice the size for sharp edges
            var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    float sum = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            sum += Mathf.Clamp01(alpha((x + 0.25f + sx * 0.5f) / sw, (y + 0.25f + sy * 0.5f) / sh));
                    px[y * sw + x] = new Color(1f, 1f, 1f, sum / 4f);
                }
            tex.SetPixels(px);
            tex.Apply();
            sp = Sprite.Create(tex, new Rect(0, 0, sw, sh), new Vector2(0.5f, 0.5f), 200f);
            Shapes[key] = sp;
            return sp;
        }

        internal static float Disc(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r ? 1f : 0f;

        private static float PillAlpha(float x, float y)
        {
            // 50 x 26: two half circles joined
            float px = x * 50f, py = y * 26f, rr = 12.5f;
            float cx = Mathf.Clamp(px, 13f, 37f);
            return (px - cx) * (px - cx) + (py - 13f) * (py - 13f) <= rr * rr ? 1f : 0f;
        }

        /// A circular arrow (reset).
        internal static Sprite ResetIcon() => Shape("reset", 24, 24, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;   // -180..180
            bool ring = r > 0.27f && r < 0.40f && !(ang > 45f && ang < 105f);
            // arrow head at the end of the ring (45 degrees), pointing on round the circle into the gap
            float c45 = Mathf.Cos(45f * Mathf.Deg2Rad), s45 = Mathf.Sin(45f * Mathf.Deg2Rad);
            float c80 = Mathf.Cos(80f * Mathf.Deg2Rad), s80 = Mathf.Sin(80f * Mathf.Deg2Rad);
            bool head = InTriangle(x, y, 0.5f + c45 * 0.19f, 0.5f + s45 * 0.19f, 0.5f + c45 * 0.48f, 0.5f + s45 * 0.48f,
                0.5f + c80 * 0.335f, 0.5f + s80 * 0.335f);
            return ring || head ? 1f : 0f;
        });

        private static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// A bell (for Ding).
        internal static Sprite BellIcon() => Shape("bell", 24, 24, (x, y) =>
        {
            float t = 1f - y;   // 0 at the top
            float w = 0f;
            if (t > 0.16f && t < 0.74f)
                w = 0.13f + 0.22f * ((t - 0.16f) / 0.58f) * ((t - 0.16f) / 0.58f) + 0.08f;
            else if (t >= 0.74f && t < 0.82f)
                w = 0.42f;
            bool body = Mathf.Abs(x - 0.5f) < w;
            bool top = Disc(x, y, 0.5f, 0.89f, 0.07f) > 0f;
            bool clapper = Disc(x, y, 0.5f, 0.1f, 0.08f) > 0f;
            return body || top || clapper ? 1f : 0f;
        });

        // ---------- colour ----------

        /// A colour swatch to click: the colour picker (and the colour's code) opens in a box under the row
        /// (put into below, right after the row).
        internal static GameObject ColourControl(RectTransform row, Transform below, Color colour, Action<Color> changed)
        {
            RectTransform box = null;
            Button sw = null;
            Action<Color> set = c =>
            {
                colour = c;
                sw.targetGraphic.color = new Color(c.r, c.g, c.b, 1f);
                changed(c);
            };
            sw = SwatchButton(row, colour, () =>
            {
                if (box != null)
                {
                    Object.DestroyImmediate(box.parent.gameObject);
                    box = null;
                    return;
                }
                box = Box(below, 40, false);
                // right under the setting's line (and its thin rule)
                Transform anchor = row;
                while (anchor.parent != null && anchor.parent != below)
                    anchor = anchor.parent;
                int at = anchor.GetSiblingIndex() + 1;
                if (at < below.childCount && below.GetChild(at).name == "Line")
                    at++;
                box.parent.SetSiblingIndex(at);
                ColourPicker(box, colour, set);
            });
            Tooltip(sw.gameObject, "Colour", "Click to pick a colour.");
            return sw.gameObject;
        }

        /// A clickable colour square with a thin dark frame.
        internal static Button SwatchButton(Transform parent, Color c, Action click, float w = 64f, float h = 26f)
        {
            RectTransform rt = HubWindow.NewRect(parent, "Swatch");
            HubWindow.Layout(rt.gameObject, w, h);
            rt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            RectTransform inner = HubWindow.NewRect(rt, "Colour");
            HubWindow.Stretch(inner, 2, 2, 2, 2);
            var img = inner.gameObject.AddComponent<Image>();
            img.color = new Color(c.r, c.g, c.b, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            b.colors = cb;
            if (click != null)
                b.onClick.AddListener(() => HubWindow.Safe(click));
            return b;
        }

        private static readonly Color[] Presets =
        {
            new Color(0f, 0f, 0f, 0.9f), new Color(0.94f, 0.88f, 0.75f, 1f), Color.white, new Color(1f, 0.78f, 0.25f, 1f),
            new Color(0.85f, 0.2f, 0.15f, 1f), new Color(0.35f, 0.75f, 0.35f, 1f), new Color(0.35f, 0.6f, 0.95f, 1f),
        };

        private static Texture2D _wheel;

        /// A colour wheel (click or drag), brightness and opacity sliders and one-click colours.
        internal static void ColourPicker(Transform parent, Color colour, Action<Color> picked)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            float a = colour.a;
            RectTransform line = Row(parent, 16f, 150f);

            RectTransform wheelRt = HubWindow.NewRect(line, "Wheel");
            HubWindow.Layout(wheelRt.gameObject, 140f, 140f);
            var raw = wheelRt.gameObject.AddComponent<RawImage>();
            raw.texture = Wheel();
            RectTransform marker = HubWindow.NewRect(wheelRt, "Marker");
            marker.sizeDelta = new Vector2(10f, 10f);
            marker.gameObject.AddComponent<Image>().color = Color.black;
            marker.GetComponent<Image>().raycastTarget = false;
            RectTransform dot = HubWindow.NewRect(marker, "Dot");
            HubWindow.Stretch(dot, 2, 2, 2, 2);
            dot.gameObject.AddComponent<Image>().raycastTarget = false;

            RectTransform side = Column(line, 6f);
            ((VerticalLayoutGroup)side.GetComponent<LayoutGroup>()).childForceExpandWidth = false;
            HubWindow.Layout(side.gameObject, 300f, -1f);
            Image preview = Swatch(side, colour, 90f, 24f);
            Slider bright = null, opacity = null;

            Action place = () =>
            {
                float ang = h * 2f * Mathf.PI;
                marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
                marker.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * s * 70f;
            };
            Action send = () =>
            {
                Color rgb = Color.HSVToRGB(h, s, v);
                var c = new Color(rgb.r, rgb.g, rgb.b, a);
                preview.color = new Color(c.r, c.g, c.b, 1f);
                place();
                picked(c);
            };
            place();

            Action<BaseEventData> onWheel = ev =>
            {
                var pe = ev as PointerEventData;
                if (pe == null)
                    return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(wheelRt, pe.position, pe.pressEventCamera ?? pe.enterEventCamera, out Vector2 local))
                    return;
                Vector2 d = (local - wheelRt.rect.center) / (wheelRt.rect.width / 2f);
                if (d.magnitude > 1.05f)
                    return;
                h = Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (2f * Mathf.PI), 1f);
                s = Mathf.Clamp01(d.magnitude);
                if (v < 0.05f)
                {
                    v = 1f;   // from black, the wheel should give that colour, not stay black
                    bright?.SetValueWithoutNotify(v);
                }
                HubWindow.Safe(send);
            };
            var trigger = wheelRt.gameObject.AddComponent<EventTrigger>();
            foreach (EventTriggerType type in new[] { EventTriggerType.PointerDown, EventTriggerType.Drag })
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(ev => onWheel(ev));
                trigger.triggers.Add(entry);
            }

            RectTransform r1 = Row(side, 8f, 26f);
            Text(r1, "Brightness", 15, Muted, false, 90f);
            bright = Slider(r1, 0f, 1f, false, v, x => { v = x; send(); }, 150f, x => Mathf.RoundToInt(x * 100f) + "%");
            RectTransform r2 = Row(side, 8f, 26f);
            Text(r2, "Opacity", 15, Muted, false, 90f);
            opacity = Slider(r2, 0f, 1f, false, a, x => { a = x; send(); }, 150f, x => Mathf.RoundToInt(x * 100f) + "%");
            RectTransform r3 = Row(side, 4f, 26f);
            foreach (Color p in Presets)
            {
                Color preset = p;
                Button b = Tile(r3, null, false, () =>
                {
                    Color.RGBToHSV(preset, out h, out s, out v);
                    a = preset.a;
                    bright?.SetValueWithoutNotify(v);
                    opacity?.SetValueWithoutNotify(a);
                    send();
                }, 24f);
                b.transform.Find("Back").GetComponent<Image>().color = new Color(preset.r, preset.g, preset.b, 1f);
            }
            RectTransform r4 = Row(side, 8f, 32f);
            Text(r4, "Code", 15, Muted, false, 90f);
            TMP_InputField code = null;
            code = Input(r4, ColorUtility.ToHtmlStringRGBA(colour), null, val =>
            {
                if (ColorUtility.TryParseHtmlString("#" + val.Trim().TrimStart('#'), out Color c))
                {
                    Color.RGBToHSV(c, out h, out s, out v);
                    a = c.a;
                    bright?.SetValueWithoutNotify(v);
                    opacity?.SetValueWithoutNotify(a);
                    send();
                }
            }, 130f);
            Action baseSend = send;
            send = () =>
            {
                baseSend();
                Color rgb = Color.HSVToRGB(h, s, v);
                code?.SetTextWithoutNotify(ColorUtility.ToHtmlStringRGBA(new Color(rgb.r, rgb.g, rgb.b, a)));
            };
        }

        /// Hue round the edge, white in the middle (made once).
        private static Texture2D Wheel()
        {
            if (_wheel != null)
                return _wheel;
            const int n = 128;
            _wheel = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float hue = Mathf.Repeat(Mathf.Atan2(dy, dx) / (2f * Mathf.PI), 1f);
                    Color col = Color.HSVToRGB(hue, Mathf.Clamp01(r), 1f);
                    col.a = Mathf.Clamp01((1f - r) * c + 0.5f);
                    px[y * n + x] = col;
                }
            _wheel.SetPixels(px);
            _wheel.Apply();
            return _wheel;
        }
    }
}
