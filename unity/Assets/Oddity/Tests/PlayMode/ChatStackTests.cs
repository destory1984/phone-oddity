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
