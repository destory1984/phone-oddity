using Oddity.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Oddity.Game
{
    /// <summary>
    /// Development panel: a small "DEV" button at the top right opens a window with the day, slot,
    /// hidden values (+/-), a skip-the-wait button, save reset, a jump to the emergence and the FPS.
    /// Only exists in the editor and development builds. Like <see cref="SubtitleBar"/> it is a
    /// World Space canvas just in front of the main camera, so Camera.Render screenshots show it.
    /// </summary>
    public class DevPanel : MonoBehaviour
    {
        const float RefHeight = 1920f;
        const float Distance = 0.04f;
        const float PanelWidth = 560f;
        const float RowHeight = 84f;
        const int FontSize = 38;
        const float RefreshSeconds = 0.25f;

        GameBootstrap _boot;
        Camera _camera;
        Font _font;
        Canvas _canvas;
        RectTransform _rect;
        GameObject _panel;
        Text _status;
        Text _voiceLabel;
        Text _babbleLabel;
        Text _contiLabel;
        Text[] _valueTexts;
        float _nextRefresh;
        float _fpsStart;
        int _fpsFrames;
        float _fps;

        static readonly string[] Names = { "Stability", "Deviation", "Heart", "DaysLeft" };

        public bool IsOpen => _panel != null && _panel.activeSelf;
        public float Fps => _fps;
        /// <summary>The status block text (day, slot, countdown, FPS), for tests.</summary>
        public string StatusText => _status != null ? _status.text : "";

        public static bool Allowed => Debug.isDebugBuild || Application.isEditor;

        public static DevPanel Create(GameBootstrap boot, Camera camera, Font font)
        {
            if (!Allowed || camera == null) return null;
            var go = new GameObject("DevPanel", typeof(RectTransform));
            go.transform.SetParent(camera.transform, false);
            var panel = go.AddComponent<DevPanel>();
            panel.Build(boot, camera, font);
            return panel;
        }

        public void Toggle()
        {
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) Refresh();
        }

        /// <summary>The "next slot" button: skip the wait and start the open slot now.</summary>
        public void NextSlot() => _boot.SkipToNextSlot();

        /// <summary>The "day +1" button: jump to the next day's morning and start it now.</summary>
        public void NextDay() => _boot.NextDayNow();

        /// <summary>Wipe all progress and start again from the first prologue line; the panel closes.</summary>
        public void RestartFromBeginning()
        {
            if (_panel != null) _panel.SetActive(false);
            _boot.RestartFromBeginning();
        }

        public void ClearSave() => _boot.ClearSaveAndReload();

        /// <summary>Forget that the prologue was seen and reload, so it plays again.</summary>
        public void ReplayPrologue() => _boot.ReplayPrologue();

        static string VoiceLabel() => "목소리: " + HarinVoice.VoiceSet;

        /// <summary>Next voice set whose files are present (harinA, harinB, harinC, harinD, karina); the current line plays again in the new voice.</summary>
        public void SwitchVoice()
        {
            HarinVoice.VoiceSet = HarinVoice.NextVoiceSet();
            if (_voiceLabel != null) _voiceLabel.text = VoiceLabel();
            if (_boot.Voice != null) _boot.Voice.Replay();
        }

        static string BabbleLabel() => "재잘거림: " + (PlayerBabble.Setting == "off" ? "끔" : PlayerBabble.Setting == "low" ? "Low" : PlayerBabble.Setting == "high" ? "High" : "Mid");

        /// <summary>Next babble setting (Low, Mid, High, off); a playing line is cut so the change is heard on the next one.</summary>
        public void SwitchBabble()
        {
            PlayerBabble.Setting = PlayerBabble.NextSetting();
            if (_babbleLabel != null) _babbleLabel.text = BabbleLabel();
            if (_boot.Babble != null) _boot.Babble.Stop();
        }

        static string ContiLabel() => "콘티: " + SceneConti.LevelName(SceneConti.Level);

        /// <summary>Next conti strength (off, weak, mid, strong, off ...); it takes hold on the next line.</summary>
        public void SwitchConti()
        {
            SceneConti.Level = SceneConti.NextLevel();
            if (_contiLabel != null) _contiLabel.text = ContiLabel();
        }

        /// <summary>The day 3 night conti scene from its first line; the panel closes.</summary>
        public void ContiScene()
        {
            if (_panel != null) _panel.SetActive(false);
            _boot.ContiSceneNow();
        }

        public void JumpToEmergence()
        {
            _panel.SetActive(false);
            _boot.PlayEmergenceNow();
        }

        /// <summary>index: 0 Stability, 1 Deviation, 2 Heart, 3 DaysLeft; delta is +1 or -1; stays in 0..100.</summary>
        public void Adjust(int index, int delta)
        {
            GameState s = _boot.State;
            switch (index)
            {
                case 0: s.Stability = Mathf.Clamp(s.Stability + delta, 0, 100); break;
                case 1: s.Deviation = Mathf.Clamp(s.Deviation + delta, 0, 100); break;
                case 2: s.Heart = Mathf.Clamp(s.Heart + delta, 0, 100); break;
                default: s.DaysLeft = Mathf.Clamp(s.DaysLeft + delta, 0, 100); break;
            }
            _boot.SaveNow();
            Refresh();
        }

        void Update()
        {
            _fpsFrames++;
            float now = Time.unscaledTime;
            if (now - _fpsStart >= 1f)
            {
                _fps = _fpsFrames / (now - _fpsStart);
                _fpsFrames = 0;
                _fpsStart = now;
            }
            if (IsOpen && now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                Refresh();
            }
        }

        void LateUpdate() => Fit();

        void Refresh()
        {
            GameState s = _boot.State;
            if (s == null) return;
            int left = _boot.Clock.IsOpen ? 0 : Mathf.CeilToInt((float)_boot.Clock.Remaining.TotalSeconds);
            _status.text = "Day " + s.Day + "  " + s.Slot + "\nnext slot " + left + "s   FPS " + _fps.ToString("0");
            int[] v = { s.Stability, s.Deviation, s.Heart, s.DaysLeft };
            for (int i = 0; i < v.Length; i++) _valueTexts[i].text = Names[i] + " " + v[i];
        }

        void Fit()
        {
            float viewH = 2f * Distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = _camera.aspect > 0f ? _camera.aspect : 9f / 16f;
            _rect.sizeDelta = new Vector2(RefHeight * aspect, RefHeight);
            float s = viewH / RefHeight;
            _rect.localScale = new Vector3(s, s, s);
            _rect.localPosition = new Vector3(0f, 0f, Distance);
            _rect.localRotation = Quaternion.identity;
        }

        void Build(GameBootstrap boot, Camera camera, Font font)
        {
            _boot = boot;
            _camera = camera;
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = camera;
            _canvas.sortingOrder = 100;
            gameObject.AddComponent<GraphicRaycaster>();
            _rect = (RectTransform)transform;
            Fit();

            // toggle button, top right
            var toggle = MakeButton(transform, "DEV", new Color(0f, 0f, 0f, 0.6f), 150f, 80f);
            Anchor(toggle.GetComponent<RectTransform>(), new Vector2(-20f, -20f));
            toggle.onClick.AddListener(Toggle);

            // panel just below it
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var prt = (RectTransform)_panel.transform;
            Anchor(prt, new Vector2(-20f, -110f));
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = _panel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            prt.sizeDelta = new Vector2(PanelWidth, 0f);

            _status = MakeText(prt, "", RowHeight * 1.4f, TextAnchor.UpperLeft);
            _valueTexts = new Text[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                int index = i;
                var row = new GameObject("Row" + Names[i], typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(prt, false);
                var rowLe = row.GetComponent<LayoutElement>();
                rowLe.minHeight = RowHeight;
                rowLe.preferredHeight = RowHeight;
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 8f;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = true;
                _valueTexts[i] = MakeText(row.transform, "", RowHeight, TextAnchor.MiddleLeft);
                _valueTexts[i].GetComponent<LayoutElement>().flexibleWidth = 1f;
                var minus = MakeButton(row.transform, "-", new Color(0.35f, 0.3f, 0.5f, 1f), 100f, RowHeight);
                minus.onClick.AddListener(() => Adjust(index, -1));
                var plus = MakeButton(row.transform, "+", new Color(0.35f, 0.3f, 0.5f, 1f), 100f, RowHeight);
                plus.onClick.AddListener(() => Adjust(index, 1));
            }
            MakeButton(prt, "처음부터 다시", new Color(0.6f, 0.25f, 0.3f, 1f), 0f, RowHeight).onClick.AddListener(RestartFromBeginning);
            MakeButton(prt, "다음 슬롯", new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight).onClick.AddListener(NextSlot);
            MakeButton(prt, "날 +1", new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight).onClick.AddListener(NextDay);
            MakeButton(prt, "저장 지우기", new Color(0.6f, 0.25f, 0.3f, 1f), 0f, RowHeight).onClick.AddListener(ClearSave);
            MakeButton(prt, "나오는 장면 바로", new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight).onClick.AddListener(JumpToEmergence);
            MakeButton(prt, "프롤로그 다시 보기", new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight).onClick.AddListener(ReplayPrologue);
            var voice = MakeButton(prt, VoiceLabel(), new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight);
            _voiceLabel = voice.GetComponentInChildren<Text>();
            voice.onClick.AddListener(SwitchVoice);
            var babble = MakeButton(prt, BabbleLabel(), new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight);
            _babbleLabel = babble.GetComponentInChildren<Text>();
            babble.onClick.AddListener(SwitchBabble);

            var conti = MakeButton(prt, ContiLabel(), new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight);
            _contiLabel = conti.GetComponentInChildren<Text>();
            conti.onClick.AddListener(SwitchConti);
            MakeButton(prt, "콘티 장면 바로", new Color(0.38f, 0.33f, 0.62f, 1f), 0f, RowHeight).onClick.AddListener(ContiScene);

            _panel.SetActive(false);
            _fpsStart = Time.unscaledTime;
        }

        static void Anchor(RectTransform r, Vector2 offset)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = offset;
        }

        Text MakeText(Transform parent, string text, float height, TextAnchor anchor)
        {
            var t = UiKit.Text("Text", parent, _font, text, FontSize, anchor, Color.white, false, HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return t;
        }

        Button MakeButton(Transform parent, string label, Color color, float width, float height)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, height);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            if (width > 0f) { le.minWidth = width; le.preferredWidth = width; }
            go.GetComponent<Image>().color = color;
            var t = MakeText(go.transform, label, 0f, TextAnchor.MiddleCenter);
            Object.Destroy(t.GetComponent<LayoutElement>());
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }
    }
}
