#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    // The camera aim has slack while it follows a cat: small moves of the cat's root (head turns, grooming, the ground snap) must not move the view
    public class CameraSlackTests
    {
        static readonly Vector3 Eye = new Vector3(0, 1, -2);
        const float Fov = 60f, Ratio = .15f;
        static float Slack(Vector3 focus) => Vector3.Distance(Eye, focus) * Mathf.Tan(Fov * .5f * Mathf.Deg2Rad) * Ratio;

        [Test]
        public void CameraSlack_AimHoldsStillWhileTheTargetJittersInsideTheCircle()
        {
            var focus = new Vector3(0, .2f, 0); var start = focus; float r = Slack(focus);
            Assert.That(r, Is.GreaterThan(.1f), "the test needs a slack circle bigger than a cat's head move");
            for (int i = 0; i < 600; i++)
            {
                var want = start + new Vector3(Mathf.Sin(i * .37f), Mathf.Sin(i * .23f), Mathf.Cos(i * .41f)) * (r * .55f);
                focus = RoomPlayRunner.SlackFollow(focus, want, Eye, Fov, Ratio, 8, 1 / 60f);
            }
            Assert.That(Vector3.Distance(focus, start), Is.LessThan(1e-5f));
        }

        [Test]
        public void CameraSlack_AimFollowsATargetThatLeavesTheCircleAndStopsAtItsEdge()
        {
            var focus = new Vector3(0, .2f, 0); var want = focus + Vector3.right * 1.5f;
            for (int i = 0; i < 600; i++) focus = RoomPlayRunner.SlackFollow(focus, want, Eye, Fov, Ratio, 8, 1 / 60f);
            float gap = Vector3.Distance(focus, want);
            Assert.That(gap, Is.EqualTo(Slack(focus)).Within(.01f), "it trails by the slack radius, it does not snap onto the target");
            Assert.That(focus.x, Is.GreaterThan(1f));
        }
    }
}
#endif
