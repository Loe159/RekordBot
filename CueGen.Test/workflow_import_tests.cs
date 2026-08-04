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
            Assert.That(errors, Has.Some.Contains("beatgrid_verified"));
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
        public void ValidatorRejectsNonCanonicalDuplicateAndUnverifiedHotCues()
        {
            var document = WorkflowImportParser.Parse(CreateJson(
                "Hot Cues",
                energy: 4,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Main Floor" },
                hotCues: new List<WorkflowHotCue>
                {
                    new() { Slot = "A", Name = "INTRO", Color = "Red", PositionMs = 0, PhraseStartVerified = false },
                    new() { Slot = "A", Name = "IN-32", Color = "Green", PositionMs = 1000, PhraseStartVerified = true },
                    new() { Slot = "H", Name = "LOOP", Color = "Cyan", PositionMs = 2000, PhraseStartVerified = true }
                }));

            var errors = new WorkflowImportValidator(WorkflowTaxonomy.LoadDefault()).Validate(document);

            Assert.That(errors, Has.Some.Contains("must be unique"));
            Assert.That(errors, Has.Some.Contains("must be named one of: INTRO"));
            Assert.That(errors, Has.Some.Contains("must use color 'Yellow'"));
            Assert.That(errors, Has.Some.Contains("first beat of a phrase"));
            Assert.That(errors, Has.Some.Contains("8- or 16-beat loop"));
        }

        [Test]
        public void ValidatorRequiresVocalEvidenceForBAndRejectsMinus16ForC()
        {
            var document = WorkflowImportParser.Parse(CreateJson(
                "Hot Cues",
                energy: 4,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Main Floor" },
                hotCues: new List<WorkflowHotCue>
                {
                    new() { Slot = "B", Name = "VOCAL", Color = "Pink", PositionMs = 30000, PhraseStartVerified = false, VocalSectionVerified = false },
                    new() { Slot = "C", Name = "DROP -16", Color = "Green", PositionMs = 60000, PhraseStartVerified = false, DropOffsetBeats = 16 }
                }));

            var errors = new WorkflowImportValidator(WorkflowTaxonomy.LoadDefault()).Validate(document);

            Assert.That(errors, Has.Some.Contains("audible four-beat vocal section"));
            Assert.That(errors, Has.Some.Contains("Hot Cue C must be named one of: DROP -32"));
            Assert.That(errors, Has.Some.Contains("drop_offset_beats must be exactly 32"));
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
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
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
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
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
            Assert.That(energyFive.Changes.Select(change => change.Field), Does.Contain("energy"));
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(5));
                Assert.That(content.ColorID, Is.EqualTo("2"));
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
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
                situations: new[] { "Peak Time" },
                hotCues: ReadyHotCues()));

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

        [Test]
        public void ReadyImportWritesCanonicalSlotsPreservesManualCuesAndIsIdempotent()
        {
            const string manualCueId = "manual-hot-cue-d";
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                var now = DateTime.UtcNow;
                database.Insert(new Cue
                {
                    ID = manualCueId,
                    ContentID = content.ID,
                    InMsec = 90000,
                    InFrame = 13500,
                    Kind = 7,
                    Color = -1,
                    ColorTableIndex = 5,
                    Comment = "MANUAL BREAK",
                    ContentUUID = content.UUID,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                });
            }

            var json = CreateJson(
                null,
                energy: 5,
                includeMood: true,
                genres: new[] { "House", "Remix" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Peak Time" },
                hotCues: ReadyHotCues(),
                beatgridVerified: true,
                quantizeVerified: true);
            var service = CreateService();
            var first = service.ImportJson(json);

            Assert.That(first.Success, Is.True, string.Join(Environment.NewLine, first.Errors));
            Assert.That(first.Changes.Select(change => change.Field), Does.Contain("hot_cues"));
            int cueCount;
            using (var database = OpenDatabase())
            {
                var cues = database.Table<Cue>().Where(cue => cue.ContentID == TargetContentId).ToList();
                cueCount = cues.Count;
                Assert.That(cues.Any(cue => cue.ID == manualCueId && cue.Comment == "MANUAL BREAK"), Is.True);

                var managed = cues
                    .Where(cue => cue.UUID != null && cue.UUID.StartsWith("e134b57e-5bc1-4554-", StringComparison.Ordinal))
                    .OrderBy(cue => cue.Kind)
                    .ToList();
                Assert.That(managed.Select(cue => cue.Kind), Is.EqualTo(new int?[] { 1, 2, 3, 5, 6, 9 }));
                Assert.That(managed.Select(cue => cue.Comment), Is.EqualTo(new[]
                {
                    "INTRO", "VOCAL", "DROP -32", "DROP 1", "BREAKDOWN", "LOOP"
                }));
                Assert.That(managed.Select(cue => cue.ColorTableIndex), Is.EqualTo(new int?[] { 32, 45, 22, 42, 56, 38 }));

                var aggregate = database.Table<ContentCue>().Single(row => row.ContentID == TargetContentId);
                var aggregateCues = JsonConvert.DeserializeObject<IList<Cue>>(aggregate.Cues);
                Assert.That(aggregate.rb_cue_count, Is.EqualTo(cues.Count));
                Assert.That(aggregateCues.Select(cue => cue.ID), Is.EquivalentTo(cues.Select(cue => cue.ID)));
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
            }

            var repeated = service.ImportJson(json);
            Assert.That(repeated.Success, Is.True, string.Join(Environment.NewLine, repeated.Errors));
            Assert.That(repeated.Changes, Is.Empty);
            using (var database = OpenDatabase())
            {
                Assert.That(database.Table<Cue>().Count(cue => cue.ContentID == TargetContentId), Is.EqualTo(cueCount));
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
            }
        }

        [Test]
        public void AllCanonicalHotCueSlotsUseExactKindsColorsAndLoopShape()
        {
            var result = CreateService().ImportJson(CreateJson(
                "Hot Cues",
                energy: 5,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Peak Time" },
                hotCues: AllHotCues()));

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            using var database = OpenDatabase();
            var managed = database.Table<Cue>()
                .Where(cue => cue.ContentID == TargetContentId)
                .ToList()
                .Where(cue => cue.UUID != null && cue.UUID.StartsWith("e134b57e-5bc1-4554-", StringComparison.Ordinal))
                .OrderBy(cue => cue.Kind)
                .ToList();
            Assert.That(managed.Select(cue => cue.Kind), Is.EqualTo(new int?[] { 1, 2, 3, 5, 6, 7, 8, 9 }));
            Assert.That(managed.Select(cue => cue.ColorTableIndex), Is.EqualTo(new int?[] { 32, 45, 22, 42, 56, 56, 45, 38 }));
            Assert.That(managed.Select(cue => cue.Comment), Is.EqualTo(new[]
            {
                "INTRO", "VOCAL", "DROP -32", "DROP 1", "BREAKDOWN", "PEAK / DROP 2", "VOCAL / HOOK", "LOOP"
            }));
            var loop = managed.Single(cue => cue.Kind == 9);
            Assert.That(loop.ActiveLoop, Is.EqualTo(1));
            Assert.That(loop.Color, Is.EqualTo(255));
            Assert.That(loop.BeatLoopSize, Is.EqualTo(0x10000 * 16 + 1));
            Assert.That(loop.OutMsec, Is.GreaterThan(loop.InMsec));
        }

        [Test]
        public void InvalidReadyRetainsHotCuesStatusWithoutMutation()
        {
            var staged = CreateService().ImportJson(CreateJson(
                "Hot Cues",
                energy: 4,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Main Floor" }));
            Assert.That(staged.Success, Is.True, string.Join(Environment.NewLine, staged.Errors));
            var before = File.ReadAllBytes(databasePath);
            var incomplete = ReadyHotCues().Where(cue => cue.Slot != "E").ToList();

            var result = CreateService().ImportJson(CreateJson(
                null,
                energy: 4,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Main Floor" },
                hotCues: incomplete,
                beatgridVerified: false,
                quantizeVerified: true));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("beatgrid_verified"));
            Assert.That(result.Errors, Has.Some.Contains("Hot Cues E"));
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(before));
            using (var database = OpenDatabase())
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
        }

        [Test]
        public void ManualHotCueCollisionStopsBeforeTransaction()
        {
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                var now = DateTime.UtcNow;
                database.Insert(new Cue
                {
                    ID = "manual-hot-cue-a",
                    ContentID = content.ID,
                    InMsec = 1000,
                    InFrame = 150,
                    Kind = 1,
                    Color = -1,
                    ContentUUID = content.UUID,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                });
            }
            var before = File.ReadAllBytes(databasePath);

            var result = CreateService().ImportJson(CreateJson(
                null,
                energy: 5,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Peak Time" },
                hotCues: ReadyHotCues(),
                beatgridVerified: true,
                quantizeVerified: true));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Manual Hot Cue A"));
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(before));
        }

        [Test]
        public void CueWriteFailureRollsBackStatusMetadataAndAggregate()
        {
            var service = CreateService();
            var staged = service.ImportJson(CreateJson(
                "Hot Cues",
                energy: 3,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Main Floor" }));
            Assert.That(staged.Success, Is.True, string.Join(Environment.NewLine, staged.Errors));

            int cueCount;
            string aggregateJson;
            using (var database = OpenDatabase())
            {
                cueCount = database.Table<Cue>().Count(cue => cue.ContentID == TargetContentId);
                aggregateJson = database.Table<ContentCue>().Single(row => row.ContentID == TargetContentId).Cues;
                database.Execute(
                    "CREATE TRIGGER fail_workflow_cue BEFORE INSERT ON djmdCue " +
                    "BEGIN SELECT RAISE(ABORT, 'forced cue failure'); END");
            }

            var result = service.ImportJson(CreateJson(
                null,
                energy: 5,
                includeMood: true,
                genres: new[] { "House" },
                yearOrigin: new[] { "2024" },
                situations: new[] { "Peak Time" },
                hotCues: ReadyHotCues(),
                beatgridVerified: true,
                quantizeVerified: true));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("forced cue failure"));
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(3));
                Assert.That(GetAssigned(database, TargetContentId, "Status"),
                    Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
                Assert.That(database.Table<Cue>().Count(cue => cue.ContentID == TargetContentId), Is.EqualTo(cueCount));
                Assert.That(database.Table<ContentCue>().Single(row => row.ContentID == TargetContentId).Cues, Is.EqualTo(aggregateJson));
            }
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
            IList<string> situations = null,
            IList<WorkflowHotCue> hotCues = null,
            bool? beatgridVerified = null,
            bool? quantizeVerified = null)
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
                BeatgridVerified = beatgridVerified,
                QuantizeVerified = quantizeVerified,
                HotCues = hotCues,
                MyTags = genres == null && yearOrigin == null && situations == null
                    ? null
                    : new WorkflowMyTags
                    {
                        Genres = genres ?? new List<string>(),
                        YearOrigin = yearOrigin ?? new List<string>(),
                        Situations = situations ?? new List<string>()
                    }
            };
            var json = JsonConvert.SerializeObject(document, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            if (status != null)
                return json;

            var ready = JObject.Parse(json);
            ready["status"] = JValue.CreateNull();
            return ready.ToString();
        }

        private static IList<WorkflowHotCue> ReadyHotCues()
        {
            return new List<WorkflowHotCue>
            {
                new() { Slot = "A", Name = "INTRO", Color = "Yellow", PositionMs = 0, PhraseStartVerified = true },
                new() { Slot = "B", Name = "VOCAL", Color = "Pink", PositionMs = 30000, PhraseStartVerified = false, VocalSectionVerified = true },
                new() { Slot = "C", Name = "DROP -32", Color = "Green", PositionMs = 60000, PhraseStartVerified = false, DropOffsetBeats = 32 },
                new() { Slot = "D", Name = "DROP 1", Color = "Red", PositionMs = 90000, PhraseStartVerified = true },
                new() { Slot = "E", Name = "BREAKDOWN", Color = "Purple", PositionMs = 120000, PhraseStartVerified = true },
                new() { Slot = "H", Name = "LOOP", Color = "Orange", PositionMs = 210000, PhraseStartVerified = true, LoopBeats = 16 }
            };
        }

        private static IList<WorkflowHotCue> AllHotCues()
        {
            return new List<WorkflowHotCue>
            {
                new() { Slot = "A", Name = "INTRO", Color = "Yellow", PositionMs = 0, PhraseStartVerified = true },
                new() { Slot = "B", Name = "VOCAL", Color = "Pink", PositionMs = 30000, PhraseStartVerified = false, VocalSectionVerified = true },
                new() { Slot = "C", Name = "DROP -32", Color = "Green", PositionMs = 60000, PhraseStartVerified = false, DropOffsetBeats = 32 },
                new() { Slot = "D", Name = "DROP 1", Color = "Red", PositionMs = 90000, PhraseStartVerified = true },
                new() { Slot = "E", Name = "BREAKDOWN", Color = "Purple", PositionMs = 120000, PhraseStartVerified = true },
                new() { Slot = "F", Name = "PEAK / DROP 2", Color = "Purple", PositionMs = 150000, PhraseStartVerified = true },
                new() { Slot = "G", Name = "VOCAL / HOOK", Color = "Pink", PositionMs = 180000, PhraseStartVerified = true },
                new() { Slot = "H", Name = "LOOP", Color = "Orange", PositionMs = 210000, PhraseStartVerified = true, LoopBeats = 16 }
            };
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
