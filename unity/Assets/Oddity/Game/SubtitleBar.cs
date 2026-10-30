using System;
using System.Collections.Generic;
using Oddity.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Oddity.Game
{
    /// <summary>
    /// Act 2 dialogue in the room: a translucent black band at the bottom of the main camera's view
    /// with one line of text. The canvas is a World Space canvas just in front of the camera, so it
    /// shows up in Camera.Render screenshots too. A tap anywhere on the view advances.
    /// </summary>
    public class SubtitleBar : MonoBehaviour
    {
        const float RefHeight = 1920f;
        const float BarHeight = 300f;
        const float BarMargin = 90f;
        const int FontSize = 46;
        const float Distance = 0.05f;

        const float ChoiceHeight = 120f;
        const float ChoiceGap = 22f;
        const int ChoiceFontSize = 42;
        static readonly Color HarinColor = new Color(0.96f, 0.95f, 0.98f, 1f);
        static readonly Color PlayerColor = new Color(0.72f, 0.84f, 1f, 1f);
        static readonly Color SystemColor = new Color(0.75f, 0.75f, 0.78f, 1f);

        public event Action Tapped;
        public event Action<int> ChoicePicked;

        Camera _camera;
        Canvas _canvas;
        RectTransform _rect;
        Text _text;
        GameObject _band;
        RectTransform _choiceArea;
        Font _font;
        readonly List<string> _choiceTexts = new List<string>();

        public string CurrentText => _text != null ? _text.text : "";
        public bool IsVisible => _canvas != null && _canvas.enabled;
        /// <summary>Texts of the choice buttons on screen (empty when none).</summary>
        public IReadOnlyList<string> VisibleChoiceTexts => _choiceTexts;

        public static SubtitleBar Create(Camera camera, Font font)
        {
            var go = new GameObject("SubtitleCanvas", typeof(RectTransform));
            go.transform.SetParent(camera.transform, false);
            var bar = go.AddComponent<SubtitleBar>();
            bar.Build(camera, font);
            return bar;
        }

        void Build(Camera camera, Font font)
        {
            _camera = camera;
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = camera;
            _canvas.sortingOrder = 10;
            gameObject.AddComponent<GraphicRaycaster>();
            _rect = (RectTransform)transform;
            _rect.localRotation = Quaternion.identity;

            // full-view transparent button: tap anywhere to advance
            var hit = new GameObject("TapArea", typeof(RectTransform), typeof(Image), typeof(Button));
            hit.transform.SetParent(transform, false);
            UiKit.Stretch((RectTransform)hit.transform);
            hit.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            hit.GetComponent<Button>().onClick.AddListener(Tap);

            var band = new GameObject("Band", typeof(RectTransform), typeof(Image));
            _band = band;
            band.transform.SetParent(transform, false);
            var br = (RectTransform)band.transform;
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(1f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.offsetMin = new Vector2(0f, BarMargin);
            br.offsetMax = new Vector2(0f, BarMargin + BarHeight);
            var img = band.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);
            img.raycastTarget = false;

            _text = UiKit.Text("Text", band.transform, _font, null, FontSize, TextAnchor.MiddleCenter, new Color(0.96f, 0.95f, 0.98f, 1f),
                false, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow);
            var tr = _text.rectTransform;
            UiKit.Stretch(tr);
            tr.offsetMin = new Vector2(60f, 30f);
            tr.offsetMax = new Vector2(-60f, -30f);
            _text.lineSpacing = 1.1f;

            // choice buttons stack upward from the band's bottom edge
            var area = new GameObject("Choices", typeof(RectTransform));
            area.transform.SetParent(transform, false);
            _choiceArea = (RectTransform)area.transform;
            _choiceArea.anchorMin = new Vector2(0f, 0f);
            _choiceArea.anchorMax = new Vector2(1f, 0f);
            _choiceArea.pivot = new Vector2(0.5f, 0f);
            _choiceArea.offsetMin = new Vector2(60f, BarMargin);
            _choiceArea.offsetMax = new Vector2(-60f, BarMargin + 2f * ChoiceHeight + ChoiceGap);

            SetVisible(false);
            Fit();
        }

        void LateUpdate() => Fit();

        // Size the canvas to exactly cover the camera's view at Distance.
        void Fit()
        {
            if (_camera == null) return;
            float viewH = 2f * Distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = _camera.aspect > 0f ? _camera.aspect : 9f / 16f;
            _rect.sizeDelta = new Vector2(RefHeight * aspect, RefHeight);
            float s = viewH / RefHeight;
            _rect.localScale = new Vector3(s, s, s);
            _rect.localPosition = new Vector3(0f, 0f, Distance);
            _rect.localRotation = Quaternion.identity;
        }

        public void SetVisible(bool visible) => _canvas.enabled = visible;

        CanvasGroup _group;

        /// <summary>Alpha of the whole bar (band, text, choices), 1 = normal. Taps still land at 0.</summary>
        public void SetAlpha(float alpha)
        {
            if (_group == null)
            {
                _group = gameObject.GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            }
            _group.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>Alpha of the bar as drawn: 0 when hidden or when the band is off and no choices show.</summary>
        public float Alpha
        {
            get
            {
                if (!IsVisible) return 0f;
                if (!_band.activeSelf && _choiceTexts.Count == 0) return 0f;
                return _group != null ? _group.alpha : 1f;
            }
        }

        public void ShowLine(Line line)
        {
            SetVisible(true);
            ClearChoices();
            _band.SetActive(true);
            _text.text = line.Text ?? "";
            _text.color = line.Who == "player" ? PlayerColor : line.Who == "system" ? SystemColor : HarinColor;
            _text.fontStyle = line.Who == "system" ? FontStyle.Italic : FontStyle.Normal;
        }

        /// <summary>Hides the band (and its text) but keeps the tap area.</summary>
        public void HideBand()
        {
            _band.SetActive(false);
        }

        /// <summary>Shows the choices as buttons over the band; picking one raises <see cref="ChoicePicked"/>.</summary>
        public void ShowChoices(IReadOnlyList<Choice> choices)
        {
            SetVisible(true);
            ClearChoices();
            _band.SetActive(false);
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                string label = choices[i].Text ?? "";
                _choiceTexts.Add(label);
                var go = new GameObject("Choice " + i, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_choiceArea, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(0f, 1f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.offsetMin = new Vector2(0f, -(i + 1) * ChoiceHeight - i * ChoiceGap);
                r.offsetMax = new Vector2(0f, -i * (ChoiceHeight + ChoiceGap));
                go.GetComponent<Image>().color = new Color(0.36f, 0.31f, 0.6f, 0.92f);
                go.GetComponent<Button>().onClick.AddListener(() => PickChoice(index));

                var t = UiKit.Text("Text", go.transform, _font, label, ChoiceFontSize, TextAnchor.MiddleCenter, Color.white, false);
                UiKit.Stretch(t.rectTransform);
            }
        }

        /// <summary>Press choice button <paramref name="index"/> (and callable from tests).</summary>
        public void PickChoice(int index)
        {
            if (index < 0 || index >= _choiceTexts.Count) return;
            ClearChoices();
            ChoicePicked?.Invoke(index);
        }

        void ClearChoices()
        {
            _choiceTexts.Clear();
            if (_choiceArea == null) return;
            for (int i = _choiceArea.childCount - 1; i >= 0; i--) Destroy(_choiceArea.GetChild(i).gameObject);
        }

        public void Tap() => Tapped?.Invoke();
    }
}
