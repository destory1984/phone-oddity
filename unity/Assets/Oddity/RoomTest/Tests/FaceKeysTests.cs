using NUnit.Framework;

namespace Oddity.RoomTest.Tests
{
    public class FaceKeysTests
    {
        [TestCase(1, "eyeSmile")] [TestCase(2, "happyClosed")] [TestCase(3, "bigSmile")] [TestCase(4, "mouthSmile")] [TestCase(5, "mouthOpen")]
        [TestCase(6, "browRaise")] [TestCase(7, "browSoft")] [TestCase(8, "browSad")] [TestCase(9, "browAngry")]
        public void FaceKeys_DigitPicksItsExpression(int digit, string name) => Assert.That(FaceKeys.Press(digit, null), Is.EqualTo(name));

        [Test] public void FaceKeys_ZeroClears() => Assert.That(FaceKeys.Press(0, "bigSmile"), Is.Null);
        [Test] public void FaceKeys_SameKeyAgainClears() => Assert.That(FaceKeys.Press(3, "bigSmile"), Is.Null);
        [Test] public void FaceKeys_OtherKeyReplaces() => Assert.That(FaceKeys.Press(8, "bigSmile"), Is.EqualTo("browSad"));
        [TestCase("face0", 0)] [TestCase("face9", 9)] [TestCase("face", -1)] [TestCase("faceX", -1)] [TestCase("1", -1)]
        public void FaceKeys_ScriptKeyParses(string key, int digit) => Assert.That(FaceKeys.ScriptDigit(key), Is.EqualTo(digit));
    }
}
