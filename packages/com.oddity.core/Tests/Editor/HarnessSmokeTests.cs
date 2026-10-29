using NUnit.Framework;
using Oddity.Core;

namespace Oddity.Core.Tests
{
    public class HarnessSmokeTests
    {
        [Test]
        public void CoreAssemblyIsReachable()
        {
            Assert.AreEqual("0.1.0", CoreInfo.Version);
        }
    }
}
