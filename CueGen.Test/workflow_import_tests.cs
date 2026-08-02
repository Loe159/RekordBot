using CueGen.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowImportTests
    {
        private const string TargetContentId = "21372204";
        private string databasePath;

        [SetUp]
        public void SetUp()
        {
            databasePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "workflow-import-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "test.db"), databasePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }

        [Test]
        public void ParserRejectsUnknownAndDuplicateProperties()
        {
            var json = CreateJson("Tags", energy: 3, includeMood: true);
            var unknown = JObject.Parse(json);
            unknown["unexpected"] = true;

            Assert.Throws<JsonSerializationException>(() => WorkflowImportParser.Parse(unknown.ToString()));
            Assert.Throws<JsonReaderException>(() => WorkflowImportParser.Parse(json.Replace(
                "\"schema_version\": \"2.0\"",
                "\"schema_version\": \"2.0\", \"schema_version\": \"2.0\"")));
        }

        [Test]
        public void ValidatorRejectsOldSchemaUnknownValuesDuplicatesAndReady()
        {
            var taxonomy = WorkflowTaxonomy.LoadDefault();
            var validator = new WorkflowImportValidator(taxonomy);
            var document = WorkflowImportParser.Parse(CreateJson(
                "Hot Cues",
                energy: 3,
                includeMood: true,
                genres: new[] { "House", "House", "Not A Genre" },
                yearOrigin: new[] { "yesterday" },
                situations: new[] { "Not A Situation" }));
            document.SchemaVersion = "1.0";
            document.Status = null;

            var errors = validator.Validate(document);

            Assert.That(errors, Has.Some.Contains("schema_version"));
            Assert.That(errors, Has.Some.Contains("phase 3 READY gate"));
            Assert.That(errors, Has.Some.Contains("must be unique"));
            Assert.That(errors, Has.Some.Contains("Not A Genre"));
            Assert.That(errors, Has.Some.Contains("yesterday"));
            Assert.That(errors, Has.Some.Contains("Not A Situation"));
        }

        [Test]
        public void ValidatorAcceptsMultipleGenresAndMoreThanThreeGroupedTags()
        {
            var document = WorkflowImportParser.Parse(CreateJson(
                "Hot Cues",
                energy: 4,
                includeMood: true,
                genres: new[] { "House", "Techno", "Remix" },
                yearOrigin: new[] { "2024", "90FR" },
                situations: new[] { "Main Floor", "Peak Time" }));

            var errors = new WorkflowImportValidator(WorkflowTaxonomy.LoadDefault()).Validate(document);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void RepeatedImportsAreIdempotentExclusiveAndPreserveUnrelatedTags()
        {
            string unrelatedRelationId;
            using (var database = OpenDatabase())
            {
                var components = database.Table<MyTag>().Single(tag => tag.Name == "Components" && tag.ParentId == "root");
                var acid = database.Table<MyTag>().Single(tag => tag.Name == "Acid" && tag.ParentId == components.ID);
                var now = DateTime.UtcNow;
                var relation = new SongMyTag
                {
                    ID = Guid.NewGuid().ToString(),
                    UUID = Guid.NewGuid().ToString(),
                    MyTagID = acid.ID,
                    ContentID = TargetContentId,
                    rb_local_usn = 9000,
                    created_at = now,
                    updated_at = now
                };
                database.Insert(relation);
                unrelatedRelationId = relation.ID;
            }

            var service = CreateService();
            var first = service.ImportJson(CreateJson(
                "Tags",
                energy: 1,
                includeMood: true,
                genres: new[] { "House", "Techno", "Remix" },
                yearOrigin: new[] { "2024", "90FR" },
                situations: new[] { "Morning", "Peak Time" }));
            Assert.That(first.Success, Is.True, string.Join(Environment.NewLine, first.Errors));

            var second = service.ImportJson(CreateJson(
                "Hot Cues",
                energy: 1,
                includeMood: true,
                genres: new[] { "House", "Techno", "Remix" },
                yearOrigin: new[] { "2024", "90FR" },
                situations: new[] { "Morning", "Peak Time" }));
            Assert.That(second.Success, Is.True, string.Join(Environment.NewLine, second.Errors));

            int tagCount;
            int relationCount;
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(1));
                Assert.That(content.ColorID, Is.EqualTo("2"));
                Assert.That(GetAssigned(database, TargetContentId, "Status"), Is.EqualTo(new[] { "Hot Cues" }));
                Assert.That(GetAssigned(database, TargetContentId, "Genre"), Is.EquivalentTo(new[] { "House", "Techno", "Remix" }));
                Assert.That(GetAssigned(database, TargetContentId, "Année"), Is.EquivalentTo(new[] { "2024", "90FR" }));
                Assert.That(GetAssigned(database, TargetContentId, "Situation"), Is.EquivalentTo(new[] { "Morning", "Peak Time" }));
                Assert.That(database.Table<SongMyTag>().Any(relation => relation.ID == unrelatedRelationId), Is.True);
                tagCount = database.Table<MyTag>().Count();
                relationCount = database.Table<SongMyTag>().Count();
            }

            var repeated = service.ImportJson(CreateJson(
                "Hot Cues",
                energy: 1,
                includeMood: true,
                genres: new[] { "House", "Techno", "Remix" },
                yearOrigin: new[] { "2024", "90FR" },
                situations: new[] { "Morning", "Peak Time" }));
            Assert.That(repeated.Success, Is.True, string.Join(Environment.NewLine, repeated.Errors));
            Assert.That(repeated.Changes, Is.Empty);

            using (var database = OpenDatabase())
            {
                Assert.That(database.Table<MyTag>().Count(), Is.EqualTo(tagCount));
                Assert.That(database.Table<SongMyTag>().Count(), Is.EqualTo(relationCount));
            }

            var energyFive = service.ImportJson(CreateJson(
                "Hot Cues",
                energy: 5,
                includeMood: true,
                genres: new[] { "House", "Techno", "Remix" },
                yearOrigin: new[] { "2024", "90FR" },
                situations: new[] { "Morning", "Peak Time" }));
            Assert.That(energyFive.Success, Is.True, string.Join(Environment.NewLine, energyFive.Errors));
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(5));
                Assert.That(content.ColorID, Is.EqualTo("2"));
            }
        }

        [Test]
        public void DryRunReportsDiffWithoutChangingDatabaseBytes()
        {
            var before = File.ReadAllBytes(databasePath);
            var service = CreateService(dryRun: true);

            var result = service.ImportJson(CreateJson(
                "Hot Cues",
                energy: 5,
                includeMood: true,
                genres: new[] { "House", "Remix" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Peak Time" }));

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.DryRun, Is.True);
            Assert.That(result.Changes, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(before));
        }

        [Test]
        public void CompleteTrackMutationRollsBackWhenOneWriteFails()
        {
            int tagCount;
            int relationCount;
            int? originalRating;
            string originalColor;
            using (var database = OpenDatabase())
            {
                tagCount = database.Table<MyTag>().Count();
                relationCount = database.Table<SongMyTag>().Count();
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                originalRating = content.Rating;
                originalColor = content.ColorID;
                database.Execute(
                    "CREATE TRIGGER fail_workflow_relation BEFORE INSERT ON djmdSongMyTag " +
                    "BEGIN SELECT RAISE(ABORT, 'forced workflow failure'); END");
            }

            var result = CreateService().ImportJson(CreateJson("Tags", energy: 5, includeMood: true));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("forced workflow failure"));
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(originalRating));
                Assert.That(content.ColorID, Is.EqualTo(originalColor));
                Assert.That(database.Table<MyTag>().Count(), Is.EqualTo(tagCount));
                Assert.That(database.Table<SongMyTag>().Count(), Is.EqualTo(relationCount));
                Assert.That(database.Table<MyTag>().Any(tag => tag.Name == "Status" && tag.ParentId == "root"), Is.False);
            }
        }

        [Test]
        public void IdentityMismatchStopsBeforeMutation()
        {
            var before = File.ReadAllBytes(databasePath);
            var document = JObject.Parse(CreateJson("Tags", energy: 3, includeMood: true));
            document["track"]["title"] = "A different track";

            var result = CreateService().ImportJson(document.ToString());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("title does not match"));
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(before));
        }

        private WorkflowImportService CreateService(bool dryRun = false)
        {
            return new WorkflowImportService(new Config
            {
                DatabasePath = databasePath,
                UseSqlCipher = false,
                DryRun = dryRun
            });
        }

        private SQLiteConnection OpenDatabase()
        {
            return new SQLiteConnection(new Generator(new Config
            {
                DatabasePath = databasePath,
                UseSqlCipher = false
            }).ConnectionString);
        }

        private string CreateJson(
            string status,
            int? energy,
            bool includeMood,
            IList<string> genres = null,
            IList<string> yearOrigin = null,
            IList<string> situations = null)
        {
            var document = new WorkflowImportDocument
            {
                SchemaVersion = "2.0",
                Track = new WorkflowTrackIdentity
                {
                    Path = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(databasePath),
                        "content",
                        "Microgainz - Mutex.mp3")),
                    Title = "Mutex"
                },
                Status = status,
                Mood = includeMood ? new WorkflowMood { Color = "Red", Label = "Énergie" } : null,
                Energy = energy,
                MyTags = genres == null && yearOrigin == null && situations == null
                    ? null
                    : new WorkflowMyTags
                    {
                        Genres = genres ?? new List<string>(),
                        YearOrigin = yearOrigin ?? new List<string>(),
                        Situations = situations ?? new List<string>()
                    }
            };
            return JsonConvert.SerializeObject(document, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        private static IList<string> GetAssigned(SQLiteConnection database, string contentId, string categoryName)
        {
            var root = database.Table<MyTag>().Single(tag => tag.Name == categoryName && tag.ParentId == "root");
            var children = database.Table<MyTag>()
                .Where(tag => tag.ParentId == root.ID)
                .ToDictionary(tag => tag.ID, tag => tag.Name);
            return database.Table<SongMyTag>()
                .Where(relation => relation.ContentID == contentId)
                .ToList()
                .Where(relation => children.ContainsKey(relation.MyTagID))
                .Select(relation => children[relation.MyTagID])
                .OrderBy(name => name)
                .ToList();
        }
    }
}
