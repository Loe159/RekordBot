using NUnit.Framework;

namespace CueGen.Test
{
    [TestFixture]
    public class BeatportTests
    {
        [Test]
        public void DefaultConfig_DoesNotContainExternalCredentials()
        {
            var config = new Config();

            Assert.That(config.BeatportUsername, Is.Null.Or.Empty);
            Assert.That(config.BeatportPassword, Is.Null.Or.Empty);
            Assert.That(config.BeatportClientId, Is.Null.Or.Empty);
            Assert.That(config.BeatportClientSecret, Is.Null.Or.Empty);
            Assert.That(config.BeatportAccessToken, Is.Null.Or.Empty);
            Assert.That(config.BeatportRefreshToken, Is.Null.Or.Empty);
            Assert.That(config.SoundchartsAppId, Is.Null.Or.Empty);
            Assert.That(config.SoundchartsApiKey, Is.Null.Or.Empty);
        }

        [Test]
        public void Constructor_DoesNotAuthenticate()
        {
            using var client = new BeatportClient(null, null);

            Assert.That(client.IsConfiguredForAuthentication, Is.False);
        }

        [Test]
        public void Authorize_WithoutCredentials_FailsBeforeNetworkAccess()
        {
            using var client = new BeatportClient(null, null);

            Assert.Throws<System.InvalidOperationException>(() => client.Authorize());
        }

        [Test]
        public void IsConfiguredForAuthentication_WithUsernameAndPassword_IsTrue()
        {
            using var client = new BeatportClient(
                nameof(Config.BeatportUsername),
                nameof(Config.BeatportPassword));

            Assert.That(client.IsConfiguredForAuthentication, Is.True);
        }
    }
}
