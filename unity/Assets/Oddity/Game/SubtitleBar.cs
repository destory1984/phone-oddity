using System;
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

        public event Action Tapped;

        Camera _camera;
        Canvas _canvas;
        RectTransform _rect;
        Text _text;

        public string CurrentText => _text != null ? _text.text : "";
        public bool IsVisible => _canvas != null && _canvas.enabled;

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
            Stretch((RectTransform)hit.transform);
            hit.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            hit.GetComponent<Button>().onClick.AddListener(Tap);

            var band = new GameObject("Band", typeof(RectTransform), typeof(Image));
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

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(band.transform, false);
            var tr = (RectTransform)tgo.transform;
            Stretch(tr);
            tr.offsetMin = new Vector2(60f, 30f);
            tr.offsetMax = new Vector2(-60f, -30f);
            _text = tgo.GetComponent<Text>();
            _text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = FontSize;
            _text.lineSpacing = 1.1f;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = new Color(0.96f, 0.95f, 0.98f, 1f);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;

            SetVisible(false);
            Fit();
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
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

        public void ShowLine(Line line)
        {
            SetVisible(true);
            _text.text = line.Text ?? "";
        }

        public void Tap() => Tapped?.Invoke();
    }
}
