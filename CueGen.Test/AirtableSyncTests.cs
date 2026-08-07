using CueGen.Workflow;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CueGen.Test
{
    [TestFixture]
    public class AirtableSyncTests
    {
        [Test]
        public void OptionsLoadTrimsValuesAndKeepsExpectedDefaults()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AirtableSyncOptions.TokenVariable] = " pat-test ",
                [AirtableSyncOptions.BaseIdVariable] = " app123 ",
                [AirtableSyncOptions.TableIdVariable] = " tbl456 ",
                [AirtableSyncOptions.ViewVariable] = " A preparer "
            };

            var options = AirtableSyncOptions.Load(name =>
                values.TryGetValue(name, out var value) ? value : null);

            Assert.That(options.Token, Is.EqualTo("pat-test"));
            Assert.That(options.BaseId, Is.EqualTo("app123"));
            Assert.That(options.TableId, Is.EqualTo("tbl456"));
            Assert.That(options.View, Is.EqualTo("A preparer"));
            Assert.That(options.StatusFieldName, Is.EqualTo("Statut"));
            Assert.That(options.PendingStatus, Is.EqualTo("À préparer dans Rekordbox"));
            Assert.That(options.ReadyStatus, Is.EqualTo("Prêt à mixer"));
        }

        [Test]
        public void OptionsLoadRejectsMissingRequiredCredentials()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                AirtableSyncOptions.Load(_ => null));

            Assert.That(exception.Message, Does.Contain(AirtableSyncOptions.TokenVariable));
            Assert.That(exception.Message, Does.Contain(AirtableSyncOptions.BaseIdVariable));
            Assert.That(exception.Message, Does.Contain(AirtableSyncOptions.TableIdVariable));
        }

        [Test]
        public void MoodAliasesMapToCanonicalWorkflowMood()
        {
            var service = CreateService();
            var warnings = new List<string>();

            var mood = (WorkflowMood)InvokePrivate(
                service,
                "MapMood",
                new List<string> { "Énergique", "Sombre" },
                warnings);

            Assert.That(mood, Is.Not.Null);
            Assert.That(mood.Label, Is.EqualTo("Énergie"));
            Assert.That(mood.Color, Is.EqualTo("Red"));
            Assert.That(warnings, Has.Some.Contains("several moods"));
        }

        [Test]
        public void SoundchartsGenreCanProduceSeveralWorkflowTags()
        {
            var service = CreateService();
            var warnings = new List<string>();

            var genres = (IList<string>)InvokePrivate(
                service,
                "MapGenres",
                "Afro House Remix",
                warnings);

            Assert.That(genres, Is.EquivalentTo(new[] { "Afro House", "House", "Remix" }));
            Assert.That(genres.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(genres.Count));
        }

        [Test]
        public void SituationAliasesMapAndUnknownValuesAreIgnored()
        {
            var service = CreateService();
            var warnings = new List<string>();

            var situations = (IList<string>)InvokePrivate(
                service,
                "MapSituations",
                new List<string> { "Peak-time", "Festival", "Unknown" },
                warnings);

            Assert.That(situations, Is.EquivalentTo(new[] { "Peak Time", "Main Floor" }));
            Assert.That(warnings, Has.Some.Contains("Unknown"));
        }

        [Test]
        public void CompleteAirtableMetadataBuildsHotCuesWorkflowStage()
        {
            var service = CreateService();
            var assembly = typeof(AirtableSyncService).Assembly;
            var sourceType = assembly.GetType("CueGen.Workflow.AirtableSourceRecord", throwOnError: true);
            var matchType = assembly.GetType("CueGen.Workflow.RekordboxTrackMatch", throwOnError: true);
            var source = Activator.CreateInstance(sourceType, nonPublic: true);
            var match = Activator.CreateInstance(matchType, nonPublic: true);

            Set(source, "Title", "Track title");
            Set(source, "Artist", "Artist name");
            Set(source, "SoundchartsGenre", "Afro House Remix");
            Set(source, "Energy", 4);
            Set(source, "Moods", new List<string> { "Énergique" });
            Set(source, "Situations", new List<string> { "Festival", "Peak-time" });

            Set(match, "Title", "Track title");
            Set(match, "Artist", "Artist name");
            Set(match, "Path", "/music/Artist name - Track title.mp3");

            var document = (WorkflowImportDocument)InvokePrivate(
                service,
                "BuildDocument",
                source,
                match,
                new List<string>());

            Assert.That(document.Status, Is.EqualTo("Hot Cues"));
            Assert.That(document.Track.Title, Is.EqualTo("Track title"));
            Assert.That(document.Track.Artist, Is.EqualTo("Artist name"));
            Assert.That(document.Mood.Label, Is.EqualTo("Énergie"));
            Assert.That(document.Energy, Is.EqualTo(4));
            Assert.That(document.MyTags.Genres, Is.EquivalentTo(new[] { "Afro House", "House", "Remix" }));
            Assert.That(document.MyTags.Situations, Is.EquivalentTo(new[] { "Main Floor", "Peak Time" }));
            Assert.That(document.HotCues, Is.Null);
        }

        private static AirtableSyncService CreateService()
        {
            return new AirtableSyncService(
                new Config { DryRun = true },
                WorkflowTaxonomy.LoadDefault(),
                new AirtableSyncOptions
                {
                    Token = "test-token",
                    BaseId = "app-test",
                    TableId = "tbl-test"
                });
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(target.GetType().FullName, methodName);
            return method.Invoke(target, arguments);
        }

        private static void Set(object target, string propertyName, object value)
        {
            target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(target, value);
        }
    }
}
