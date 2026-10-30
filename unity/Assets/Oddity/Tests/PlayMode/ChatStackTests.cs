using System.Collections;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>Chat bubbles stack from the top of the chat area and follow the newest line when it fills.</summary>
    public class ChatStackTests
    {
        GameObject _go;
        bool _typing;

        [SetUp]
        public void SetUp()
        {
            _typing = ChatView.TypingEnabled;
            ChatView.TypingEnabled = false;
            _go = new GameObject("ChatStackTest", typeof(RectTransform), typeof(ChatView));
        }

        [TearDown]
        public void TearDown()
        {
            ChatView.TypingEnabled = _typing;
            if (_go != null) Object.Destroy(_go);
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        void Add(ChatView chat, int n)
        {
            for (int i = 0; i < n; i++)
                chat.ShowLine(new Line { Who = i % 2 == 0 ? "harin" : "system", Text = "테스트 줄 " + i });
        }

        // Task 27: Harin's window is taller (960 of 1920); three choices still fit on the screen under the chat,
        // and the bubbles of a full chat do not overlap.
        [UnityTest]
        public IEnumerator Chat_WindowTallerAndChoicesFit()
        {
            yield return null;
            var chat = _go.GetComponent<ChatView>();
            Assert.GreaterOrEqual(ChatView.WindowHeight, 900f);
            var root = WorldRect((RectTransform)_go.transform);
            var window = WorldRect((RectTransform)_go.transform.Find("HarinWindow"));
            Assert.AreEqual(ChatView.WindowHeight / ChatView.CanvasHeight, window.height / root.height, 1e-3f, "window share");
            Add(chat, 12);
            chat.ShowLine(new Line { Who = "system", Text = "설치가 끝났습니다." });
            chat.ShowChoices(new[]
            {
                new Choice { Text = "응, 나도 반가워. 오늘 처음이지?" },
                new Choice { Text = "누구세요?" },
                new Choice { Text = "앱이 말을 하네" },
            });
            yield return null;
            Assert.AreEqual(3, chat.VisibleButtonTexts.Count);
            var viewport = (RectTransform)_go.transform.Find("ChatViewport");
            var vr = WorldRect(viewport);
            Assert.Greater(vr.height, 0.2f * root.height, "chat area keeps a fifth of the screen");
            Assert.LessOrEqual(vr.yMax, window.yMin + 0.5f, "chat under the window");
            var choices = _go.transform.Find("BottomArea/Choices");
            Assert.IsNotNull(choices);
            int buttons = 0;
            foreach (Transform b in choices)
            {
                if (!b.gameObject.activeSelf || b.GetComponent<UnityEngine.UI.Button>() == null) continue;
                buttons++;
                var r = WorldRect((RectTransform)b);
                Assert.GreaterOrEqual(r.yMin, root.yMin - 0.5f, b.name + " cut at the bottom");
                Assert.LessOrEqual(r.yMax, vr.yMin + 0.5f, b.name + " over the chat");
                Assert.GreaterOrEqual(r.xMin, root.xMin - 0.5f);
                Assert.LessOrEqual(r.xMax, root.xMax + 0.5f);
            }
            Assert.AreEqual(3, buttons);
            var content = viewport.Find("Content");
            for (int i = 1; i < content.childCount; i++)
            {
                var above = WorldRect((RectTransform)content.GetChild(i - 1));
                var below = WorldRect((RectTransform)content.GetChild(i));
                Assert.LessOrEqual(below.yMax, above.yMin + 0.5f, "bubbles " + (i - 1) + " and " + i + " overlap");
            }
        }

        [UnityTest]
        public IEnumerator Chat_FirstLinesHugTop()
        {
            yield return null;
            var chat = _go.GetComponent<ChatView>();
            Add(chat, 2);
            yield return null;
            var viewport = (RectTransform)_go.transform.Find("ChatViewport");
            var first = WorldRect((RectTransform)viewport.Find("Content").GetChild(0));
            float gap = WorldRect(viewport).yMax - first.yMax;
            Assert.That(gap, Is.InRange(0f, 60f), "first bubble top gap " + gap);
        }

        [UnityTest]
        public IEnumerator Chat_ScrollsToNewestWhenFull()
        {
            yield return null;
            var chat = _go.GetComponent<ChatView>();
            Add(chat, 20);
            yield return null;
            var viewport = (RectTransform)_go.transform.Find("ChatViewport");
            var content = viewport.Find("Content");
            var vr = WorldRect(viewport);
            var last = WorldRect((RectTransform)content.GetChild(content.childCount - 1));
            Assert.That(last.yMin, Is.GreaterThanOrEqualTo(vr.yMin - 0.5f), "last bubble below the chat area");
            Assert.That(last.yMax, Is.LessThanOrEqualTo(vr.yMax + 0.5f), "last bubble above the chat area");
        }
    }
}
