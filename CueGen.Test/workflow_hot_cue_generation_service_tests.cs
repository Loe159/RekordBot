using CueGen.Workflow;
using Newtonsoft.Json;
using NUnit.Framework;
using SQLite;
using System;
using System.IO;
using System.Linq;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowHotCueGenerationServiceTests
    {
        private const string TargetContentId = "21372204";
        private string databasePath;

        [SetUp]
        public void SetUp()
        {
            databasePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "workflow-hot-cues-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "test.db"), databasePath);
            using var database = OpenDatabase();
            database.CreateTable<Playlist>();
            database.CreateTable<SongPlaylist>();
            var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
            content.DisableQuantize = 0;
            database.Update(content);
            StageMetadata();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }

        [Test]
        public void DryRunAndWriteAreIdempotentAndPreserveManualCue()
        {
            const string manualCueId = "manual-workflow-f";
            const string manualMemoryCueId = "manual-workflow-memory";
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                var now = DateTime.UtcNow;
                database.Insert(new Cue
                {
                    ID = manualCueId,
                    ContentID = content.ID,
                    InMsec = 10000,
                    InFrame = 1500,
                    Kind = 7,
                    Color = -1,
                    Comment = "MANUAL F",
                    ContentUUID = content.UUID,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                });
                database.Insert(new Cue
                {
                    ID = manualMemoryCueId,
                    ContentID = content.ID,
                    InMsec = 12000,
                    InFrame = 1800,
                    Kind = 0,
                    Color = -1,
                    Comment = "MANUAL MEMORY",
                    ContentUUID = content.UUID,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                });
            }

            var before = File.ReadAllBytes(databasePath);
            var dryRun = CreateService(dryRun: true).Generate();
            Assert.That(dryRun.Success, Is.True, string.Join(Environment.NewLine, dryRun.Errors.Concat(dryRun.Tracks.SelectMany(track => track.Errors))));
            Assert.That(dryRun.SelectedCount, Is.EqualTo(1));
            Assert.That(dryRun.Tracks.Single().Changes, Is.Not.Empty);
            Assert.That(dryRun.Tracks.Single().Changes.Single(change => change.Field == "status").After,
                Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(before));

            var written = CreateService().Generate();
            Assert.That(written.Success, Is.True, string.Join(Environment.NewLine, written.Tracks.SelectMany(track => track.Errors)));
            Assert.That(written.Tracks.Single().HotCues.Select(cue => cue.Slot), Does.Contain("A"));
            Assert.That(written.Tracks.Single().HotCues.Select(cue => cue.Slot), Does.Not.Contain("B"));
            Assert.That(written.Tracks.Single().Warnings, Has.Some.Contains("no analyzed vocal stem content row"));
            using (var database = OpenDatabase())
            {
                Assert.That(database.Table<Cue>().Any(cue => cue.ID == manualCueId && cue.Comment == "MANUAL F"), Is.True);
                Assert.That(database.Table<Cue>().Any(cue =>
                    cue.ID == manualMemoryCueId && cue.Comment == "MANUAL MEMORY"), Is.True);
                var memoryCues = database.Table<Cue>()
                    .Where(cue => cue.ContentID == TargetContentId && cue.Kind == 0)
                    .OrderBy(cue => cue.InMsec)
                    .ToList();
                Assert.That(memoryCues, Has.Count.LessThanOrEqualTo(WorkflowMemoryCueRuleEngine.MaximumMemoryCues));
                Assert.That(memoryCues.Any(cue => cue.Comment == WorkflowMemoryCueRuleEngine.ManualVocalName), Is.True);
                var safety = memoryCues.Single(cue => cue.Comment == WorkflowMemoryCueRuleEngine.SafetyLoopName);
                Assert.That(safety.BeatLoopSize, Is.EqualTo(0x10000 * 4 + 1));
                Assert.That(new RekordboxWorkflowRepository(database).IsContentCueConsistent(TargetContentId), Is.True);
                Assert.That(GetStatus(database), Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
            }

            var repeated = CreateService().Generate();
            Assert.That(repeated.Success, Is.True, string.Join(Environment.NewLine, repeated.Tracks.SelectMany(track => track.Errors)));
            Assert.That(repeated.Tracks.Single().Changes, Is.Empty);
            using var verification = OpenDatabase();
            Assert.That(GetStatus(verification), Is.EqualTo(new[] { WorkflowImportService.ReviewStatus }));
        }

        [Test]
        public void PreservesMovedVocalCueAcrossRepeatedGeneration()
        {
            var first = CreateService().Generate();
            Assert.That(first.Success, Is.True, string.Join(Environment.NewLine, first.Tracks.SelectMany(track => track.Errors)));

            const int movedPositionMs = 45000;
            using (var database = OpenDatabase())
            {
                var vocal = database.Table<Cue>().Single(cue =>
                    cue.ContentID == TargetContentId &&
                    cue.Kind == 0 &&
                    cue.Comment == WorkflowMemoryCueRuleEngine.ManualVocalName);
                vocal.InMsec = movedPositionMs;
                vocal.InFrame = (int)(movedPositionMs * 150.0 / 1000.0);
                vocal.updated_at = DateTime.UtcNow;
                database.Update(vocal);
            }

            var afterMove = CreateService().Generate();
            Assert.That(afterMove.Success, Is.True, string.Join(Environment.NewLine, afterMove.Tracks.SelectMany(track => track.Errors)));
            using (var database = OpenDatabase())
            {
                var vocal = database.Table<Cue>().Single(cue =>
                    cue.ContentID == TargetContentId &&
                    cue.Kind == 0 &&
                    cue.Comment == WorkflowMemoryCueRuleEngine.ManualVocalName);
                Assert.That(vocal.InMsec, Is.EqualTo(movedPositionMs));
                Assert.That(new RekordboxWorkflowRepository(database).IsContentCueConsistent(TargetContentId), Is.True);
            }

            var repeated = CreateService().Generate();
            Assert.That(repeated.Success, Is.True, string.Join(Environment.NewLine, repeated.Tracks.SelectMany(track => track.Errors)));
            Assert.That(repeated.Tracks.Single().Changes, Is.Empty);
        }

        [Test]
        public void BroadSelectionProcessesOriginalTrackButNotItsStemRows()
        {
            using (var database = OpenDatabase())
            {
                var stem = database.Table<Content>().First(item => item.ID != TargetContentId);
                stem.FolderPath = "content/Microgainz - Mutex_vocal.mp3";
                database.Update(stem);
            }

            var config = CreateConfig();
            config.FileGlob = "**/Microgainz - Mutex*.mp3";
            config.DryRun = true;

            var result = new WorkflowHotCueGenerationService(config).Generate();

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors.Concat(result.Tracks.SelectMany(track => track.Errors))));
            Assert.That(result.SelectedCount, Is.EqualTo(1));
            Assert.That(result.Tracks.Single().ContentId, Is.EqualTo(TargetContentId));
        }

        [Test]
        public void ReadsAnalyzedVocalStemAndLeavesAnalysisFilesUnchanged()
        {
            string datPath;
            string extPath;
            using (var database = OpenDatabase())
            {
                var original = database.Table<Content>().Single(item => item.ID == TargetContentId);
                var stem = database.Table<Content>().First(item => item.ID != TargetContentId);
                var directory = Path.GetDirectoryName(original.FolderPath);
                var fileName = Path.GetFileNameWithoutExtension(original.FolderPath);
                stem.FolderPath = Path.Combine(directory ?? string.Empty, fileName + "_vocal.mp3").Replace('\\', '/');
                stem.AnalysisDataPath = original.AnalysisDataPath;
                stem.Analysed = 1;
                database.Update(stem);

                datPath = Path.Join(Path.GetDirectoryName(databasePath), "share", original.AnalysisDataPath);
                extPath = Path.ChangeExtension(datPath, ".EXT");
            }
            var datBefore = File.ReadAllBytes(datPath);
            var extBefore = File.ReadAllBytes(extPath);

            var result = CreateService().Generate();

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors.Concat(result.Tracks.SelectMany(track => track.Errors))));
            var vocal = result.Tracks.Single().HotCues.Single(cue => cue.Slot == "B");
            Assert.That(vocal.Name, Is.EqualTo("VOCAL"));
            Assert.That(vocal.Color, Is.EqualTo("Pink"));
            Assert.That(vocal.VocalSectionVerified, Is.True);
            Assert.That(File.ReadAllBytes(datPath), Is.EqualTo(datBefore));
            Assert.That(File.ReadAllBytes(extPath), Is.EqualTo(extBefore));
        }

        [Test]
        public void ReadsAnalyzedVocalStemWhenRekordboxStoresAnAbsolutePath()
        {
            using (var database = OpenDatabase())
            {
                var original = database.Table<Content>().Single(item => item.ID == TargetContentId);
                var stem = database.Table<Content>().First(item => item.ID != TargetContentId);
                var directory = Path.GetDirectoryName(original.FolderPath);
                var fileName = Path.GetFileNameWithoutExtension(original.FolderPath);
                stem.FolderPath = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(databasePath),
                    directory ?? string.Empty,
                    fileName + "_vocal.mp3"));
                stem.AnalysisDataPath = original.AnalysisDataPath;
                stem.Analysed = 1;
                database.Update(stem);
            }

            var result = CreateService().Generate();

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors.Concat(result.Tracks.SelectMany(track => track.Errors))));
            Assert.That(result.Tracks.Single().HotCues.Select(cue => cue.Slot), Does.Contain("B"));
            Assert.That(result.Tracks.Single().Warnings, Does.Not.Contain(Does.Contain("no vocal stem waveform analysis was provided")));
        }

        [Test]
        public void CueFailureRollsBackTheTrackAndReportsTheError()
        {
            using (var database = OpenDatabase())
            {
                database.Execute(
                    "CREATE TRIGGER fail_generated_workflow_cue BEFORE INSERT ON djmdCue " +
                    "BEGIN SELECT RAISE(ABORT, 'forced generated cue failure'); END");
            }

            var result = CreateService().Generate();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Tracks.Single().Errors, Has.Some.Contains("forced generated cue failure"));
            using var verification = OpenDatabase();
            Assert.That(
                verification.Table<Cue>().Count(cue =>
                    cue.ContentID == TargetContentId &&
                    cue.UUID != null &&
                    cue.UUID.StartsWith("e134b57e-5bc1-4554-", StringComparison.Ordinal)),
                Is.EqualTo(0));
            Assert.That(verification.Table<Playlist>().Count(), Is.EqualTo(0));
            Assert.That(GetStatus(verification), Is.EqualTo(new[] { "Hot Cues" }));
        }

        private void StageMetadata()
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
                Status = "Hot Cues",
                Mood = new WorkflowMood { Color = "Red", Label = WorkflowTaxonomy.LoadDefault().Moods.Single(item => item.Color == "Red").Label },
                Energy = 4,
                MyTags = new WorkflowMyTags
                {
                    Genres = new[] { "House" },
                    YearOrigin = new[] { "2024" },
                    Situations = new[] { "Main Floor" }
                }
            };
            var result = new WorkflowImportService(CreateConfig(), WorkflowTaxonomy.LoadDefault())
                .ImportJson(JsonConvert.SerializeObject(document));
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));

            using var database = OpenDatabase();
            var taxonomy = WorkflowTaxonomy.LoadDefault();
            database.RunInTransaction(() => new RekordboxWorkflowRepository(database).SyncCategory(
                TargetContentId,
                taxonomy.Categories.Status,
                new[] { "Hot Cues" }));
        }

        private WorkflowHotCueGenerationService CreateService(bool dryRun = false)
        {
            var config = CreateConfig();
            config.DryRun = dryRun;
            return new WorkflowHotCueGenerationService(config);
        }

        private Config CreateConfig()
        {
            return new Config
            {
                DatabasePath = databasePath,
                UseSqlCipher = false,
                FileGlob = "**/Microgainz - Mutex.mp3"
            };
        }

        private SQLiteConnection OpenDatabase()
        {
            return new SQLiteConnection(new Generator(CreateConfig()).ConnectionString);
        }

        private static string[] GetStatus(SQLiteConnection database)
        {
            var root = database.Table<MyTag>().Single(tag => tag.Name == "Status" && tag.ParentId == "root");
            var names = database.Table<MyTag>()
                .Where(tag => tag.ParentId == root.ID)
                .ToDictionary(tag => tag.ID, tag => tag.Name);
            return database.Table<SongMyTag>()
                .Where(relation => relation.ContentID == TargetContentId)
                .ToList()
                .Where(relation => names.ContainsKey(relation.MyTagID))
                .Select(relation => names[relation.MyTagID])
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
