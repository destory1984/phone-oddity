using System;
using System.Collections;
using System.Collections.Generic;
using Oddity.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oddity.Game
{
    /// <summary>
    /// Messenger UI on the phone's World Space canvas (1080x1920 reference). The whole hierarchy is
    /// built in Awake so the scene only needs this component, a few asset references and the canvas.
    /// Uses the legacy UI Text: the built-in font falls back to the OS font, which has Hangul.
    /// </summary>
    public class ChatView : MonoBehaviour
    {
        public const float CanvasWidth = 1080f;
        public const float CanvasHeight = 1920f;
        const float WindowHeight = 760f;
        const float ChoiceAreaHeight = 470f;
        const float NoticeHeight = 190f;
        const float NoticeSeconds = 3f;
        const float BubbleMaxTextWidth = 640f;
        const int BodyFontSize = 40;

        [SerializeField] Font font;
        [SerializeField] RenderTexture harinTexture;
        [SerializeField] Material glitchMaterial;
        [SerializeField] Sprite wallpaper;
        [SerializeField] Sprite appIcon;

        public event Action Tapped;
        public event Action<int> ChoicePicked;
        public event Action<string> NameEntered;

        Font _font;
        Material _glitch;
        RectTransform _root;
        RectTransform _content;
        ScrollRect _scroll;
        RectTransform _choiceArea;
        RectTransform _choiceList;
        RectTransform _namePanel;
        InputField _nameInput;
        Text _waitingText;
        RectTransform _notice;
        Text _noticeText;
        Coroutine _noticeRoutine;
        readonly List<Button> _buttons = new List<Button>();
        readonly List<string> _buttonTexts = new List<string>();
        int _lastWaitingShown = int.MinValue;

        /// <summary>Text of the line most recently passed to <see cref="ShowLine"/>, or "".</summary>
        public string CurrentLineText { get; private set; } = "";
        /// <summary>Who spoke the current line ("harin", "player", "system"), or "".</summary>
        public string CurrentLineWho { get; private set; } = "";
        /// <summary>Texts of the choice or feeling buttons now on screen.</summary>
        public IReadOnlyList<string> VisibleButtonTexts => _buttonTexts;
        /// <summary>True while the visible buttons are feeling buttons.</summary>
        public bool FeelingMode { get; private set; }
        /// <summary>Whole seconds shown on the waiting screen, or -1 when it is not shown.</summary>
        public int WaitingSeconds { get; private set; } = -1;
        public bool IsAskingName { get; private set; }
        public bool IsLockNoticeVisible => _notice != null && _notice.gameObject.activeSelf;
        public string LockNoticeText => _noticeText != null ? _noticeText.text : "";
        public int BubbleCount => _content != null ? _content.childCount : 0;
        public float GlitchAmount { get; private set; }
        /// <summary>Font used by the messenger (Pretendard in the scene), for other UI to match.</summary>
        public Font Font => _font != null ? _font : font;

        /// <summary>Shows or hides the whole phone UI without stopping its components.</summary>
        public void SetVisible(bool visible)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = visible;
        }

        public bool IsVisible
        {
            get
            {
                var canvas = GetComponent<Canvas>();
                return canvas == null || canvas.enabled;
            }
        }

        void Awake()
        {
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = (RectTransform)transform;
            Build();
        }

        // ---------------------------------------------------------------- public API

        public void ShowLine(Line line)
        {
            CurrentLineText = line.Text ?? "";
            CurrentLineWho = line.Who ?? "";
            HideBottom();
            AddBubble(line.Who, line.Text);
        }

        public void ShowChoices(IReadOnlyList<Choice> choices, bool feeling = false)
        {
            HideBottom();
            FeelingMode = feeling;
            _choiceArea.gameObject.SetActive(true);
            _choiceList.gameObject.SetActive(true);
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                string text = choices[i].Text ?? "";
                var button = MakeButton(_choiceList, text, feeling ? new Color(0.62f, 0.52f, 0.86f, 1f) : new Color(0.38f, 0.33f, 0.62f, 1f), 120f);
                button.onClick.AddListener(() => PickChoice(index));
                _buttons.Add(button);
                _buttonTexts.Add(text);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceList);
        }

        public void ShowWaiting(TimeSpan remaining)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)remaining.TotalSeconds));
            if (WaitingSeconds < 0)
            {
                HideBottom();
                _choiceArea.gameObject.SetActive(true);
                _waitingText.gameObject.SetActive(true);
                _lastWaitingShown = int.MinValue;
            }
            WaitingSeconds = seconds;
            if (_lastWaitingShown != seconds)
            {
                _lastWaitingShown = seconds;
                _waitingText.text = "다음 이야기까지\n" + seconds + "초";
            }
        }

        public void ShowLockNotice(string text)
        {
            _noticeText.text = text ?? "";
            if (_noticeRoutine != null) StopCoroutine(_noticeRoutine);
            _noticeRoutine = StartCoroutine(NoticeRoutine());
        }

        public void AskName()
        {
            HideBottom();
            IsAskingName = true;
            _choiceArea.gameObject.SetActive(true);
            _namePanel.gameObject.SetActive(true);
            _nameInput.text = "";
            _nameInput.ActivateInputField();
        }

        /// <summary>Raised by a tap on the phone screen (and callable from tests).</summary>
        public void Tap() => Tapped?.Invoke();

        /// <summary>Press choice button <paramref name="index"/> (and callable from tests).</summary>
        public void PickChoice(int index)
        {
            if (index < 0 || index >= _buttonTexts.Count) return;
            string text = _buttonTexts[index];
            HideBottom();
            AddBubble("player", text);
            ChoicePicked?.Invoke(index);
        }

        /// <summary>Confirm the name field (and callable from tests).</summary>
        public void SubmitName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || !IsAskingName) return;
            HideBottom();
            NameEntered?.Invoke(name);
        }

        /// <summary>Glitch strength 0..1 for Harin's window.</summary>
        public void SetGlitch(float amount)
        {
            GlitchAmount = Mathf.Clamp01(amount);
            if (_glitch != null) _glitch.SetFloat("_Glitch", GlitchAmount);
        }

        // ---------------------------------------------------------------- building

        void Build()
        {
            _root.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            // wallpaper doubles as the tap catcher
            var bg = MakeImage("Wallpaper", _root, wallpaper != null ? Color.white : new Color(0.89f, 0.86f, 0.96f, 1f));
            bg.sprite = wallpaper;
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            bg.gameObject.AddComponent<TapCatcher>().Init(this);

            // Harin's window (top)
            var window = new GameObject("HarinWindow", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            window.transform.SetParent(_root, false);
            var wrt = window.rectTransform;
            wrt.anchorMin = new Vector2(0f, 1f);
            wrt.anchorMax = new Vector2(1f, 1f);
            wrt.pivot = new Vector2(0.5f, 1f);
            wrt.sizeDelta = new Vector2(0f, WindowHeight);
            wrt.anchoredPosition = Vector2.zero;
            window.texture = harinTexture;
            float vis = WindowHeight / CanvasWidth; // square texture, show a band of it
            window.uvRect = new Rect(0f, 1f - vis, 1f, vis);
            if (glitchMaterial != null)
            {
                _glitch = new Material(glitchMaterial);
                window.material = _glitch;
            }
            window.raycastTarget = false;

            // chat history
            var viewport = new GameObject("ChatViewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect));
            viewport.transform.SetParent(_root, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(0f, ChoiceAreaHeight);
            vrt.offsetMax = new Vector2(0f, -WindowHeight);
            var vimg = viewport.GetComponent<Image>();
            vimg.color = new Color(1f, 1f, 1f, 0f);
            vimg.raycastTarget = true;
            viewport.AddComponent<TapCatcher>().Init(this);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(vrt, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(1f, 0f);
            _content.pivot = new Vector2(0.5f, 0f);
            _content.sizeDelta = Vector2.zero;
            _content.anchoredPosition = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 18f;
            vlg.padding = new RectOffset(36, 36, 24, 24);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.LowerCenter;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = viewport.GetComponent<ScrollRect>();
            _scroll.viewport = vrt;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            // bottom area: choices, waiting, name input
            var area = MakeImage("BottomArea", _root, new Color(0.96f, 0.94f, 1f, 0.92f));
            _choiceArea = area.rectTransform;
            _choiceArea.anchorMin = Vector2.zero;
            _choiceArea.anchorMax = new Vector2(1f, 0f);
            _choiceArea.pivot = new Vector2(0.5f, 0f);
            _choiceArea.sizeDelta = new Vector2(0f, ChoiceAreaHeight);
            _choiceArea.anchoredPosition = Vector2.zero;
            area.raycastTarget = true;

            var list = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(_choiceArea, false);
            _choiceList = (RectTransform)list.transform;
            Stretch(_choiceList);
            var clg = list.GetComponent<VerticalLayoutGroup>();
            clg.padding = new RectOffset(60, 60, 30, 30);
            clg.spacing = 24f;
            clg.childControlWidth = true;
            clg.childControlHeight = true;
            clg.childForceExpandWidth = true;
            clg.childForceExpandHeight = false;
            clg.childAlignment = TextAnchor.MiddleCenter;

            _waitingText = MakeText("Waiting", _choiceArea, "", 56, TextAnchor.MiddleCenter, new Color(0.3f, 0.26f, 0.45f, 1f));
            Stretch(_waitingText.rectTransform);
            _waitingText.raycastTarget = false;

            BuildNamePanel();
            BuildNotice();
            HideBottom();
            _choiceArea.gameObject.SetActive(false);
        }

        void BuildNamePanel()
        {
            var panel = new GameObject("NamePanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(_choiceArea, false);
            _namePanel = (RectTransform)panel.transform;
            Stretch(_namePanel);
            var lg = panel.GetComponent<VerticalLayoutGroup>();
            lg.padding = new RectOffset(60, 60, 40, 40);
            lg.spacing = 24f;
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = true;
            lg.childForceExpandHeight = false;
            lg.childAlignment = TextAnchor.MiddleCenter;

            var label = MakeText("NameLabel", _namePanel, "이름을 알려 주세요", 44, TextAnchor.MiddleCenter, new Color(0.3f, 0.26f, 0.45f, 1f));
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
            label.raycastTarget = false;

            var fieldImage = MakeImage("NameInput", _namePanel, Color.white);
            fieldImage.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
            _nameInput = fieldImage.gameObject.AddComponent<InputField>();
            var inputText = MakeText("Text", fieldImage.rectTransform, "", BodyFontSize, TextAnchor.MiddleLeft, new Color(0.15f, 0.12f, 0.25f, 1f));
            Stretch(inputText.rectTransform);
            inputText.rectTransform.offsetMin = new Vector2(24f, 6f);
            inputText.rectTransform.offsetMax = new Vector2(-24f, -6f);
            inputText.supportRichText = false;
            var placeholder = MakeText("Placeholder", fieldImage.rectTransform, "이름", BodyFontSize, TextAnchor.MiddleLeft, new Color(0.6f, 0.58f, 0.7f, 1f));
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(24f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-24f, -6f);
            _nameInput.textComponent = inputText;
            _nameInput.placeholder = placeholder;
            _nameInput.characterLimit = 12;
            _nameInput.lineType = InputField.LineType.SingleLine;

            var ok = MakeButton(_namePanel, "확인", new Color(0.38f, 0.33f, 0.62f, 1f), 110f);
            ok.onClick.AddListener(() => SubmitName(_nameInput.text));
        }

        void BuildNotice()
        {
            var band = MakeImage("LockNotice", _root, new Color(0.12f, 0.1f, 0.2f, 0.94f));
            _notice = band.rectTransform;
            _notice.anchorMin = new Vector2(0f, 1f);
            _notice.anchorMax = new Vector2(1f, 1f);
            _notice.pivot = new Vector2(0.5f, 1f);
            _notice.sizeDelta = new Vector2(-60f, NoticeHeight);
            _notice.anchoredPosition = new Vector2(0f, -30f);
            band.raycastTarget = false;

            var icon = MakeImage("AppIcon", _notice, Color.white);
            icon.sprite = appIcon;
            icon.enabled = appIcon != null;
            var irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(90f, 90f);
            irt.anchoredPosition = new Vector2(36f, 0f);
            icon.raycastTarget = false;

            _noticeText = MakeText("NoticeText", _notice, "", 42, TextAnchor.MiddleLeft, Color.white);
            var trt = _noticeText.rectTransform;
            Stretch(trt);
            trt.offsetMin = new Vector2(150f, 10f);
            trt.offsetMax = new Vector2(-30f, -10f);
            _noticeText.raycastTarget = false;
            _noticeText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _notice.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- behaviour

        void HideBottom()
        {
            foreach (var b in _buttons)
            {
                if (b == null) continue;
                b.gameObject.SetActive(false);
                Destroy(b.gameObject);
            }
            _buttons.Clear();
            _buttonTexts.Clear();
            FeelingMode = false;
            WaitingSeconds = -1;
            IsAskingName = false;
            if (_waitingText != null) _waitingText.gameObject.SetActive(false);
            if (_namePanel != null) _namePanel.gameObject.SetActive(false);
            if (_choiceList != null) _choiceList.gameObject.SetActive(false);
            if (_choiceArea != null) _choiceArea.gameObject.SetActive(false);
        }

        void AddBubble(string who, string text)
        {
            text = text ?? "";
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_content, false);
            var rlg = row.GetComponent<HorizontalLayoutGroup>();
            rlg.childControlWidth = true;
            rlg.childControlHeight = true;
            rlg.childForceExpandWidth = false;
            rlg.childForceExpandHeight = false;

            bool system = who == "system";
            bool player = who == "player";
            rlg.childAlignment = system ? TextAnchor.UpperCenter : (player ? TextAnchor.UpperRight : TextAnchor.UpperLeft);

            Color fill = system ? new Color(0.78f, 0.77f, 0.82f, 1f)
                : player ? new Color(0.99f, 0.9f, 0.45f, 1f)
                : Color.white;
            var bubble = MakeImage("Bubble", (RectTransform)row.transform, fill);
            bubble.raycastTarget = false;
            var blg = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
            int padX = system ? 22 : 28;
            int padY = system ? 10 : 18;
            blg.padding = new RectOffset(padX, padX, padY, padY);
            blg.childControlWidth = true;
            blg.childControlHeight = true;
            blg.childForceExpandWidth = false;
            blg.childForceExpandHeight = false;

            int size = system ? 30 : BodyFontSize;
            var label = MakeText("Text", bubble.rectTransform, text, size, TextAnchor.MiddleLeft,
                system ? new Color(0.25f, 0.24f, 0.3f, 1f) : new Color(0.12f, 0.1f, 0.2f, 1f));
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            float maxW = system ? 820f : BubbleMaxTextWidth;
            float width = Mathf.Min(label.preferredWidth, maxW);
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Ceil(width) + 2f;

            while (_content.childCount > 40) DestroyImmediate(_content.GetChild(0).gameObject);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = 0f;
        }

        IEnumerator NoticeRoutine()
        {
            _notice.gameObject.SetActive(true);
            const float slide = 0.25f;
            Vector2 shown = new Vector2(0f, -30f);
            Vector2 hidden = new Vector2(0f, NoticeHeight + 10f);
            for (float t = 0f; t < slide; t += Time.unscaledDeltaTime)
            {
                _notice.anchoredPosition = Vector2.Lerp(hidden, shown, t / slide);
                yield return null;
            }
            _notice.anchoredPosition = shown;
            yield return new WaitForSecondsRealtime(NoticeSeconds);
            for (float t = 0f; t < slide; t += Time.unscaledDeltaTime)
            {
                _notice.anchoredPosition = Vector2.Lerp(shown, hidden, t / slide);
                yield return null;
            }
            _notice.gameObject.SetActive(false);
            _noticeRoutine = null;
        }

        // ---------------------------------------------------------------- helpers

        Image MakeImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        Text MakeText(string name, RectTransform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            return t;
        }

        Button MakeButton(RectTransform parent, string text, Color color, float height)
        {
            var img = MakeImage("Button", parent, color);
            img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var label = MakeText("Text", img.rectTransform, text, BodyFontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
            label.raycastTarget = false;
            return button;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Forwards a click on the object to <see cref="ChatView.Tap"/>.</summary>
        class TapCatcher : MonoBehaviour, IPointerClickHandler
        {
            ChatView _view;
            public void Init(ChatView view) => _view = view;
            public void OnPointerClick(PointerEventData eventData) => _view.Tap();
        }
    }
}
