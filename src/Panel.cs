// The Rúnakefli's panel: beside the build menu (Valheim 1.0's BuildUi, Hud.m_buildUi; the old m_pieceSelectionWindow is
// never shown: test 0.2.1 had the panel inside it, unseen) while it is open, sliders for the next rune's colour and size, and for an
// etched rune its glow. Built like PotatoDye's cape panel (the game's own background and text copied, sliders styled the
// Valheim way by Jötunn). It follows the menu's open tab (Painted or Etched); settings are kept with the character until
// changed (the user, 2026-10-30).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotatoPainter
{
    internal static class Settings
    {
        // Glow: a share of the colour's cap (GlowCap, 0.4.4), 25% to 100%.
        internal const float SizeMin = 0.25f, SizeMax = 2f, GlowMin = 0.25f, GlowMax = 1f;
        // An etched rune's strokes: its glow colour at this share, so the default (ember) comes out close to the red ochre
        // the Rúnakefli's own etched word has under its glow.
        internal const float EtchedSurface = 0.62f;
        private const string SaveKey = "PotatoPainter.Panel";

        internal static float PaintH, PaintS, PaintV, EtchH, EtchS, Glow, Size;
        // An etched rune's own light (RuneLight), on by default (the user, 2026-10-31).
        internal static bool Light = true;
        // Goes up on every change, so the placement preview redraws.
        internal static int Version;

        static Settings() => Defaults(true, true);

        // Painted: red ochre. Etched: ember at strength 1 (the Rúnakefli's own glow, probe test 22). 0.5 m.
        internal static void Defaults(bool painted, bool etched)
        {
            if (painted) Color.RGBToHSV(Look.RedOchre, out PaintH, out PaintS, out PaintV);
            if (etched) { Color.RGBToHSV(Look.Ember, out EtchH, out EtchS, out _); Glow = 1f; Light = true; }
            Size = Paint.DefaultSize;
            Version++;
        }

        internal static Color PaintColor => Opaque(Color.HSVToRGB(PaintH, PaintS, PaintV));
        internal static Color GlowColor => Opaque(Color.HSVToRGB(EtchH, EtchS, 1f));
        internal static Color EtchColor => Opaque(GlowColor * EtchedSurface);
        // Kept with the paint: the glow's hue at full brightness times its share of the cap.
        internal static Color KeptGlow => Opaque(GlowColor * Glow);
        // Drawn: that share of the cap for this colour and the bloom setting now.
        internal static Color Emission => GlowCap.Emission(KeptGlow, true);

        internal static Color Colour(bool etched) => etched ? EtchColor : PaintColor;
        internal static Color KeptFor(bool etched) => etched ? KeptGlow : Color.black;
        internal static Color GlowFor(bool etched) => etched ? Emission : Color.black;

        private static Color Opaque(Color c) { c.a = 1f; return c; }

        // Kept with the character as "ph=0.013;ps=0.903;...".
        internal static void Load(Player player)
        {
            Defaults(true, true);
            if (!player || !player.m_customData.TryGetValue(SaveKey, out var text)) return;
            float oldStrength = -1f; bool share = false;
            foreach (var entry in text.Split(';'))
            {
                int split = entry.IndexOf('=');
                if (split <= 0 || !float.TryParse(entry.Substring(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;
                switch (entry.Substring(0, split))
                {
                    case "ph": PaintH = Mathf.Clamp01(v); break;
                    case "ps": PaintS = Mathf.Clamp01(v); break;
                    case "pv": PaintV = Mathf.Clamp01(v); break;
                    case "eh": EtchH = Mathf.Clamp01(v); break;
                    case "es": EtchS = Mathf.Clamp01(v); break;
                    case "eg": oldStrength = v; break;   // before 0.4.4: a strength, 0.25 to 2
                    case "gs": Glow = Mathf.Clamp(v, GlowMin, GlowMax); share = true; break;
                    case "sz": Size = Mathf.Clamp(v, SizeMin, SizeMax); break;
                    case "el": Light = v != 0f; break;
                }
            }
            // A strength kept before 0.4.4 becomes the same strength as a share of this colour's cap (with bloom, the lower one).
            if (!share && oldStrength > 0f) Glow = Mathf.Clamp(oldStrength / GlowCap.Cap(GlowColor, true), GlowMin, GlowMax);
            Version++;
        }

        internal static void Save(Player player)
        {
            if (!player) return;
            string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            player.m_customData[SaveKey] = $"ph={F(PaintH)};ps={F(PaintS)};pv={F(PaintV)};eh={F(EtchH)};es={F(EtchS)};gs={F(Glow)};sz={F(Size)};el={(Light ? 1 : 0)}";
            Version++;
        }
    }

    internal static class Panel
    {
        private const int StripWidth = 64;
        private sealed class Row { internal TMP_Text Label, Value; internal Slider Slider; internal Texture2D Strip; internal Image StripImage; }

        private static GameObject panel;
        private static Hud owner;
        private static TMP_Text title;
        private static readonly Row[] rows = new Row[4];
        private static Image swatch;
        private static GameObject lightButton;
        private static bool syncing;
        private static bool? shown;   // the kind the sliders are laid out for (true = etched)
        private static Player loadedFor;
        private static readonly Func<BuildUi, GameObject> CurrentTagButton = HarmonyLib.AccessTools.MethodDelegate<Func<BuildUi, GameObject>>(HarmonyLib.AccessTools.Method(typeof(BuildUi), "GetCurrentTagButton"));

        // Every frame (Plugin.LateUpdate): shown while the Rúnakefli's build menu is open.
        internal static void Update()
        {
            var player = Player.m_localPlayer;
            var hud = Hud.instance;
            // The character's settings, as soon as it is in the world (the preview uses them before the panel opens).
            if (player && loadedFor != player) { Settings.Load(player); loadedFor = player; }
            bool open = player && hud && Plugin.Table && player.InPlaceMode() && Plugin.BuildPieces(player) == Plugin.Table && Hud.IsPieceSelectionVisible();
            if (!open)
            {
                if (panel && panel.activeSelf) panel.SetActive(false);
                return;
            }
            try
            {
                if (!panel || owner != hud) Build(hud);
                if (!panel) return;
                if (!panel.activeSelf)
                {
                    panel.SetActive(true);
                    shown = null;
                    Fit();
                }
                bool etched = EtchedShown(player);
                if (shown != etched) { shown = etched; Show(etched); }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("The Rúnakefli's panel is off this session: " + e);
                if (panel) UnityEngine.Object.Destroy(panel);
                panel = null; owner = hud;
            }
        }

        // The open sidebar entry's kind (BuildUi.GetCurrentTagButton: its display name is Jötunn's token for the tab,
        // "$jotunn_cat_elder_futhark__etched", or the tab's name); with Show All, Recent and the like, the selected rune's.
        private static bool EtchedShown(Player player)
        {
            var button = Hud.instance && Hud.instance.m_buildUi ? CurrentTagButton(Hud.instance.m_buildUi) : null;
            var tag = button ? button.GetComponent<BuildUiTagButton>() : null;
            var name = tag ? (tag.m_displayName ?? "").ToLowerInvariant() : "";
            if (name.EndsWith("etched")) return true;
            if (name.EndsWith("painted")) return false;
            var piece = player.GetSelectedPiece();
            var paint = piece ? piece.GetComponent<Paint>() : null;
            return paint && paint.Etched;
        }

        // Painted: hue, saturation, brightness, size. Etched: hue, saturation, glow, size.
        private static void Show(bool etched)
        {
            title.text = etched ? "Etched" : "Painted";
            string[] names = etched ? new[] { "Hue", "Saturation", "Glow", "Size" } : new[] { "Hue", "Saturation", "Brightness", "Size" };
            syncing = true;
            for (int i = 0; i < 4; i++) rows[i].Label.text = names[i];
            Range(rows[0].Slider, 0f, 1f, etched ? Settings.EtchH : Settings.PaintH);
            Range(rows[1].Slider, 0f, 1f, etched ? Settings.EtchS : Settings.PaintS);
            if (etched) Range(rows[2].Slider, Settings.GlowMin, Settings.GlowMax, Settings.Glow);
            else Range(rows[2].Slider, 0f, 1f, Settings.PaintV);
            Range(rows[3].Slider, Settings.SizeMin, Settings.SizeMax, Settings.Size);
            syncing = false;
            Refresh();
        }

        private static void Range(Slider s, float min, float max, float value)
        {
            s.minValue = min; s.maxValue = max; s.value = Mathf.Clamp(value, min, max);
        }

        private static void Changed()
        {
            if (syncing || shown == null) return;
            bool etched = shown.Value;
            if (etched) { Settings.EtchH = rows[0].Slider.value; Settings.EtchS = rows[1].Slider.value; Settings.Glow = rows[2].Slider.value; }
            else { Settings.PaintH = rows[0].Slider.value; Settings.PaintS = rows[1].Slider.value; Settings.PaintV = rows[2].Slider.value; }
            Settings.Size = Mathf.Round(rows[3].Slider.value * 20f) / 20f;   // 5 cm steps
            Settings.Save(Player.m_localPlayer);
            Refresh();
        }

        private static void Refresh()
        {
            bool etched = shown == true;
            float h = rows[0].Slider.value, s = rows[1].Slider.value, third = rows[2].Slider.value;
            rows[0].Value.text = Mathf.RoundToInt(h * 360f).ToString(CultureInfo.InvariantCulture) + "°";
            rows[1].Value.text = Mathf.RoundToInt(s * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            rows[2].Value.text = Mathf.RoundToInt(third * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            rows[3].Value.text = Settings.Size.ToString("0.00", CultureInfo.InvariantCulture) + " m";
            swatch.color = etched ? Settings.GlowColor : Settings.PaintColor;
            if (lightButton)
            {
                lightButton.SetActive(etched);
                string label = "Light: " + (Settings.Light ? "On" : "Off");
                var tmp = lightButton.GetComponentInChildren<TMP_Text>(true);
                if (tmp) tmp.text = label;
                else { var legacy = lightButton.GetComponentInChildren<Text>(true); if (legacy) legacy.text = label; }
            }
            // Each strip shows what moving its slider does: every hue, then grey to full colour, then dark to light (painted)
            // or the glow from faint to its cap (GlowCap).
            Paint(rows[0], x => Color.HSVToRGB(x, 1f, 1f));
            Paint(rows[1], x => Color.HSVToRGB(h, x, etched ? 1f : Mathf.Max(third, 0.35f)));
            float cap = GlowCap.Cap(Color.HSVToRGB(h, s, 1f), GlowCap.Bloom);
            if (etched) Paint(rows[2], x => Color.HSVToRGB(h, s, Mathf.Clamp01(Mathf.Lerp(Settings.GlowMin, Settings.GlowMax, x) * cap)));
            else Paint(rows[2], x => Color.HSVToRGB(h, s, x));
        }

        private static void Paint(Row row, Func<float, Color> at)
        {
            if (!row.Strip) return;
            for (int x = 0; x < StripWidth; x++) row.Strip.SetPixel(x, 0, at(x / (StripWidth - 1f)));
            row.Strip.Apply(false);
        }

        private static void ToggleLight()
        {
            if (shown != true) return;
            Settings.Light = !Settings.Light;
            Settings.Save(Player.m_localPlayer);
            Refresh();
        }

        private static void Reset()
        {
            if (shown == null) return;
            Settings.Defaults(!shown.Value, shown.Value);
            Settings.Save(Player.m_localPlayer);
            Show(shown.Value);
        }

        // ------------------------------------------------------------------ building
        private static void Build(Hud hud)
        {
            if (panel) UnityEngine.Object.Destroy(panel);
            panel = null; owner = hud;
            var ui = hud.m_buildUi ? hud.m_buildUi.transform as RectTransform : null;
            var textSource = hud.m_pieceDescription;
            if (!ui || !textSource)
            {
                Plugin.Log.LogWarning("The build menu changed; the Rúnakefli's panel stays off.");
                return;
            }
            // The window itself: the build menu's largest picture (its wood background), unless that fills the screen.
            var canvas = ui.GetComponentInParent<Canvas>()?.rootCanvas;
            var source = ui.GetComponentsInChildren<Image>(true)
                .Where(image => image.sprite && !image.GetComponentInParent<Selectable>())
                .OrderByDescending(image => ((RectTransform)image.transform).rect.width * ((RectTransform)image.transform).rect.height).FirstOrDefault();
            var window = ui;
            if (source && canvas && ((RectTransform)source.transform).rect.width < ((RectTransform)canvas.transform).rect.width * 0.8f) window = (RectTransform)source.transform;
            const float width = 340f, pad = 16f, labelWidth = 96f, valueWidth = 58f, rowHeight = 34f;
            float height = pad + 34f + rowHeight * 4f + 50f + pad;

            panel = new GameObject("PotatoPainter_Panel", typeof(RectTransform));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(window, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);
            rect.sizeDelta = new Vector2(width, height);

            // The build window's own background, or a plain dark one.
            var background = panel.AddComponent<Image>();
            if (source) { background.sprite = source.sprite; background.type = source.type; background.color = source.color; background.material = source.material; background.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier; }
            else background.color = new Color(0f, 0f, 0f, 0.75f);
            background.raycastTarget = true;   // clicks on the panel don't fall through to what closes the menu

            float y = pad;
            title = Text(textSource, "Title", pad, y, width - pad * 2f, 30f);
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = textSource.fontSize * 1.2f;
            y += 34f;

            for (int i = 0; i < 4; i++)
            {
                var row = new Row();
                row.Label = Text(textSource, "Label" + i, pad, y, labelWidth, 26f);
                row.Label.alignment = TextAlignmentOptions.Left;

                var sliderObject = UnityEngine.UI.DefaultControls.CreateSlider(GUIManager.Instance.ValheimControlResources);
                sliderObject.name = "PotatoPainter_Slider" + i;
                sliderObject.layer = panel.layer;
                foreach (Transform t in sliderObject.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = panel.layer;
                sliderObject.transform.SetParent(rect, false);
                var slider = sliderObject.GetComponent<Slider>();
                GUIManager.Instance.ApplySliderStyle(slider);
                slider.wholeNumbers = false;
                slider.onValueChanged.AddListener(_ => Changed());
                Place((RectTransform)sliderObject.transform, pad + labelWidth + 4f, y + 4f, width - pad * 2f - labelWidth - 8f - valueWidth, 18f);
                row.Slider = slider;
                if (i < 3) Strip(row);

                row.Value = Text(textSource, "Value" + i, width - pad - valueWidth, y, valueWidth, 26f);
                row.Value.alignment = TextAlignmentOptions.Right;
                rows[i] = row;
                y += rowHeight;
            }

            var swatchObject = new GameObject("PotatoPainter_Swatch", typeof(RectTransform));
            swatchObject.layer = panel.layer;
            swatchObject.transform.SetParent(rect, false);
            Place((RectTransform)swatchObject.transform, pad, y + 8f, 40f, 30f);
            swatch = swatchObject.AddComponent<Image>();
            swatch.raycastTarget = false;

            var reset = GUIManager.Instance.CreateButton("Default", rect, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, 130f, 36f);
            reset.name = "PotatoPainter_Default";
            Place((RectTransform)reset.transform, width - pad - 130f, y + 6f, 130f, 36f);
            reset.GetComponent<Button>().onClick.AddListener(Reset);
            // Etched only: the rune's own light, on or off (RuneLight).
            lightButton = GUIManager.Instance.CreateButton("Light: On", rect, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, 130f, 36f);
            lightButton.name = "PotatoPainter_Light";
            Place((RectTransform)lightButton.transform, pad + 44f, y + 6f, 130f, 36f);
            lightButton.GetComponent<Button>().onClick.AddListener(ToggleLight);
            panel.SetActive(false);
        }

        // Colour sliders: the track and fill give way to a strip of the colours they reach; the handle stays.
        private static void Strip(Row row)
        {
            var slider = row.Slider;
            var handle = slider.handleRect;
            foreach (var part in slider.GetComponentsInChildren<Image>(true))
                if (!handle || !part.transform.IsChildOf(handle)) part.enabled = false;
            row.Strip = new Texture2D(StripWidth, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PotatoPainter strip" };
            var holder = new GameObject("PotatoPainter_Strip", typeof(RectTransform));
            holder.layer = panel.layer;
            var r = (RectTransform)holder.transform;
            r.SetParent(slider.transform, false);
            r.SetAsFirstSibling();
            r.anchorMin = new Vector2(0f, 0.2f);
            r.anchorMax = new Vector2(1f, 0.8f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            row.StripImage = holder.AddComponent<Image>();
            row.StripImage.sprite = Sprite.Create(row.Strip, new Rect(0f, 0f, StripWidth, 1f), new Vector2(0.5f, 0.5f));
            row.StripImage.raycastTarget = true;
        }

        // Keep the whole panel on screen.
        private static void Fit()
        {
            var rect = (RectTransform)panel.transform;
            rect.anchoredPosition = new Vector2(12f, 0f);
            var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas) return;
            Canvas.ForceUpdateCanvases();
            var space = (RectTransform)canvas.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var corner in corners)
            {
                var local = space.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y); maxY = Mathf.Max(maxY, local.y);
            }
            var bounds = space.rect;
            float dx = 0f, dy = 0f;
            if (maxX > bounds.xMax) dx = bounds.xMax - maxX;
            if (minX + dx < bounds.xMin) dx = bounds.xMin - minX;
            if (minY < bounds.yMin) dy = bounds.yMin - minY;
            if (maxY + dy > bounds.yMax) dy = bounds.yMax - maxY;
            if (dx == 0f && dy == 0f) return;
            var shift = rect.parent.InverseTransformVector(space.TransformVector(new Vector3(dx, dy, 0f)));
            rect.anchoredPosition += new Vector2(shift.x, shift.y);
        }

        private static TMP_Text Text(TMP_Text source, string name, float x, float y, float w, float h)
        {
            var copy = UnityEngine.Object.Instantiate(source.gameObject, panel.transform, false);
            copy.name = "PotatoPainter_" + name;
            foreach (var localize in copy.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(localize);
            foreach (var layout in copy.GetComponents<LayoutElement>()) UnityEngine.Object.Destroy(layout);
            foreach (var fitter in copy.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(fitter);
            foreach (Transform child in copy.transform) UnityEngine.Object.Destroy(child.gameObject);
            var text = copy.GetComponent<TMP_Text>();
            text.text = "";
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            Place((RectTransform)copy.transform, x, y, w, h);
            return text;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
