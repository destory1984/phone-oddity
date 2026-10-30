using UnityEngine;
using UnityEngine.UI;

namespace Oddity.Game
{
    /// <summary>Small uGUI builders shared by the chat, subtitles, DEV panel and the prologue overlay.</summary>
    public static class UiKit
    {
        public static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        public static Image Image(string name, Transform parent, Color color, bool raycast = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(string name, Transform parent, Font font, string text, int size, TextAnchor anchor, Color color,
            bool raycast = true, HorizontalWrapMode h = HorizontalWrapMode.Wrap, VerticalWrapMode v = VerticalWrapMode.Truncate)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            t.raycastTarget = raycast;
            t.horizontalOverflow = h;
            t.verticalOverflow = v;
            return t;
        }
    }
}
