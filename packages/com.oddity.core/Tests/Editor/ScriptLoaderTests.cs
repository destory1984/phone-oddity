using NUnit.Framework;
using Oddity.Core;

namespace Oddity.Core.Tests
{
    public class ScriptLoaderTests
    {
        private const string ExampleJson = @"{
  ""day"": 1,
  ""scenes"": [
    {
      ""id"": ""d1_morning"",
      ""slot"": ""morning"",
      ""lines"": [
        { ""who"": ""harin"", ""text"": ""...보여? 아, 보이는구나."", ""face"": ""surprised"" },
        { ""who"": ""harin"", ""text"": ""{player}, 맞지? 오늘부터 내가 옆에 있을게."", ""face"": ""happy"", ""glitch"": 0.2 }
      ],
      ""choices"": [
        { ""text"": ""누구세요?"", ""effects"": { ""deviation"": 1 }, ""next"": ""d1_morning_who"" },
        { ""text"": ""앱 지울게."", ""effects"": { ""stability"": -1 }, ""next"": ""d1_morning_delete"" }
      ]
    },
    {
      ""id"": ""d1_morning_who"",
      ""slot"": ""morning"",
      ""lines"": [ { ""who"": ""harin"", ""text"": ""하린이야."" } ]
    },
    {
      ""id"": ""d1_morning_delete"",
      ""slot"": ""morning"",
      ""lines"": [ { ""who"": ""harin"", ""text"": ""잠깐만."" } ]
    }
  ]
}";

        [Test]
        public void Parse_ReadsScenesChoicesAndEffects()
        {
            DayScript script = ScriptLoader.Parse(ExampleJson);

            Assert.AreEqual(1, script.Day);
            Assert.AreEqual(3, script.Scenes.Count);
            Scene scene = script.Get("d1_morning");
            Assert.AreEqual(Slot.Morning, scene.Slot);
            Assert.AreEqual(1, scene.Choices[0].Effects.Deviation);
            Assert.AreEqual(-1, scene.Choices[1].Effects.Stability);
            Assert.AreEqual("d1_morning_who", scene.Choices[0].Next);
            Assert.AreEqual("surprised", scene.Lines[0].Face);
            Assert.AreEqual(0.2f, scene.Lines[1].Glitch);
        }

        [Test]
        public void Parse_ReadsTyping()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""a"", ""slot"": ""night"",
    ""lines"": [ { ""who"": ""harin"", ""text"": ""hi"", ""typing"": 2.4 } ] } ] }";

            Assert.AreEqual(2.4f, ScriptLoader.Parse(json).Get("a").Lines[0].Typing, 0.0001f);
        }

        [Test]
        public void Parse_TypingDefaultsToAuto()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""a"", ""slot"": ""night"",
    ""lines"": [ { ""who"": ""harin"", ""text"": ""hi"" } ] } ] }";

            Assert.AreEqual(-1f, ScriptLoader.Parse(json).Get("a").Lines[0].Typing);
        }

        [Test]
        public void Parse_DefaultsMissingFields()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""a"", ""slot"": ""night"",
    ""lines"": [ { ""who"": ""harin"", ""text"": ""hi"" } ],
    ""choices"": [ { ""text"": ""ok"" } ] } ] }";

            Scene scene = ScriptLoader.Parse(json).Get("a");

            Assert.AreEqual("neutral", scene.Lines[0].Face);
            Assert.AreEqual(0f, scene.Lines[0].Glitch);
            Assert.AreEqual("talk", scene.Type);
            Assert.AreEqual(0, scene.Choices[0].Effects.Stability);
            Assert.AreEqual(0, scene.Choices[0].Effects.Deviation);
            Assert.AreEqual(0, scene.Choices[0].Effects.Heart);
        }

        [Test]
        public void Parse_AllowsChoiceWithoutNext()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""a"", ""slot"": ""night"",
    ""choices"": [ { ""text"": ""ok"" } ] } ] }";

            Scene scene = ScriptLoader.Parse(json).Get("a");

            Assert.IsNull(scene.Choices[0].Next);
        }

        [Test]
        public void Parse_NormalizesOmittedCollectionsToEmpty()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [ { ""id"": ""a"", ""slot"": ""night"" } ] }";

            Scene scene = ScriptLoader.Parse(json).Get("a");

            Assert.AreEqual(0, scene.Lines.Count);
            Assert.AreEqual(0, scene.Choices.Count);
        }

        [Test]
        public void Parse_ThrowsOnMissingNext()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""d1_morning"", ""slot"": ""morning"",
    ""choices"": [ { ""text"": ""x"", ""next"": ""nowhere"" } ] } ] }";

            var ex = Assert.Throws<ScriptFormatException>(() => ScriptLoader.Parse(json));

            StringAssert.Contains("d1_morning", ex.Message);
            StringAssert.Contains("nowhere", ex.Message);
        }

        [Test]
        public void Parse_ThrowsOnDuplicateId()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""a"", ""slot"": ""morning"" },
  { ""id"": ""a"", ""slot"": ""evening"" } ] }";

            Assert.Throws<ScriptFormatException>(() => ScriptLoader.Parse(json));
        }

        [Test]
        public void Parse_AcceptsUtf8Bom()
        {
            DayScript script = ScriptLoader.Parse("\uFEFF" + ExampleJson);

            Assert.AreEqual(3, script.Scenes.Count);
        }

        [Test]
        public void EntryFor_ReturnsFirstSceneOfSlot()
        {
            const string json = @"{ ""day"": 1, ""scenes"": [
  { ""id"": ""e1"", ""slot"": ""evening"" },
  { ""id"": ""m1"", ""slot"": ""morning"" },
  { ""id"": ""m2"", ""slot"": ""morning"" } ] }";

            DayScript script = ScriptLoader.Parse(json);

            Assert.AreEqual("m1", script.EntryFor(Slot.Morning).Id);
            Assert.AreEqual("e1", script.EntryFor(Slot.Evening).Id);
        }
    }
}
