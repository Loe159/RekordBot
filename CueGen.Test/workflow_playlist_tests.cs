using CueGen.Workflow;
using Newtonsoft.Json;
using NUnit.Framework;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowPlaylistTests
    {
        private const string TargetContentId = "21372204";
        private string databasePath;

        [SetUp]
        public void SetUp()
        {
            databasePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "workflow-playlists-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "test.db"), databasePath);
            using var database = OpenDatabase();
            database.CreateTable<Playlist>();
            database.CreateTable<SongPlaylist>();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }

        [Test]
        public void ValidatorRequiresExactCanonicalPlaylistPlan()
        {
            var document = CreateDocument(
                "Hot Cues",
                "Mutex",
                "Microgainz - Mutex.mp3",
                new[] { "House", "Techno" });
            document.DesiredPlaylists.Remove("Genre/Techno");
            document.DesiredPlaylists.Add("Genre/Rap");

            var errors = new WorkflowImportValidator(WorkflowTaxonomy.LoadDefault()).Validate(document);

            Assert.That(errors, Has.Some.Contains("missing: Genre/Techno"));
            Assert.That(errors, Has.Some.Contains("unexpected paths: Genre/Rap"));
        }

        [Test]
        public void EveryStatusTransitionKeepsOnePreparationAndOverlappingClassifications()
        {
            var statuses = new string[] { "To Do", "Mood", "Energy", "Tags", "Hot Cues", null };
            foreach (var status in statuses)
            {
                var document = CreateProgressDocument(status);
                var result = CreateService().ImportJson(JsonConvert.SerializeObject(document));

                Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
                using var database = OpenDatabase();
                var paths = new RekordboxWorkflowRepository(database).GetManagedPlaylistPaths(TargetContentId);
                Assert.That(
                    paths.Where(path => path.StartsWith("Preparation/", StringComparison.Ordinal)),
                    Is.EqualTo(new[] { "Preparation/" + (status ?? WorkflowPlaylistPlan.ReadyName) }));
                Assert.That(paths, Is.EquivalentTo(document.DesiredPlaylists));
            }

            using (var database = OpenDatabase())
            {
                var paths = new RekordboxWorkflowRepository(database).GetManagedPlaylistPaths(TargetContentId);
                Assert.That(paths, Does.Contain("Genre/House"));
                Assert.That(paths, Does.Contain("Genre/Techno"));
                Assert.That(paths, Does.Contain("Situation/Main Floor"));
                Assert.That(paths, Does.Contain("Situation/Peak Time"));
            }

            int playlistCount;
            int relationCount;
            using (var database = OpenDatabase())
            {
                playlistCount = database.Table<Playlist>().Count();
                relationCount = database.Table<SongPlaylist>().Count();
            }

            var repeated = CreateService().ImportJson(JsonConvert.SerializeObject(CreateProgressDocument(null)));
            Assert.That(repeated.Success, Is.True, string.Join(Environment.NewLine, repeated.Errors));
            Assert.That(repeated.Changes, Is.Empty);
            using (var database = OpenDatabase())
            {
                Assert.That(database.Table<Playlist>().Count(), Is.EqualTo(playlistCount));
                Assert.That(database.Table<SongPlaylist>().Count(), Is.EqualTo(relationCount));
            }
        }

        [Test]
        public void MainMusicalFamiliesShareClassificationPlaylistsAndPreserveUserPlaylist()
        {
            string userRelationId;
            using (var database = OpenDatabase())
            {
                var now = DateTime.UtcNow;
                var folder = new Playlist
                {
                    ID = "90000001",
                    Name = "User Lists",
                    Attribute = 1,
                    ParentID = "root",
                    Seq = 1,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                };
                var playlist = new Playlist
                {
                    ID = "90000002",
                    Name = "Favorites",
                    Attribute = 0,
                    ParentID = folder.ID,
                    Seq = 1,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                };
                var relation = new SongPlaylist
                {
                    ID = Guid.NewGuid().ToString(),
                    PlaylistID = playlist.ID,
                    ContentID = TargetContentId,
                    TrackNo = 1,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now,
                    updated_at = now
                };
                database.Insert(folder);
                database.Insert(playlist);
                database.Insert(relation);
                userRelationId = relation.ID;
            }

            var tracks = new[]
            {
                (Title: "Mutex", File: "Microgainz - Mutex.mp3", Genre: "House"),
                (Title: "Vertex", File: "Microgainz - Vertex.mp3", Genre: "Techno"),
                (Title: "Effigy", File: "Microgainz - Effigy.mp3", Genre: "Rap"),
                (Title: "LambdaX", File: "Microgainz - LambdaX.mp3", Genre: "Pop"),
                (Title: "Monad", File: "Microgainz - Monad.mp3", Genre: "Open Format")
            };
            foreach (var track in tracks)
            {
                var document = CreateDocument(null, track.Title, track.File, new[] { track.Genre });
                var result = CreateService().ImportJson(JsonConvert.SerializeObject(document));
                Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            }

            using var verification = OpenDatabase();
            Assert.That(verification.Table<SongPlaylist>().Any(item => item.ID == userRelationId), Is.True);
            var playlists = verification.Table<Playlist>().ToDictionary(item => item.ID);
            var relations = verification.Table<SongPlaylist>().ToList();
            var ready = playlists.Values.Single(item => item.Name == "READY" && item.Attribute == 0);
            Assert.That(relations.Count(item => item.PlaylistID == ready.ID), Is.EqualTo(tracks.Length));
            var energyFive = playlists.Values.Single(item => item.Name == "5" && item.Attribute == 0);
            Assert.That(relations.Count(item => item.PlaylistID == energyFive.ID), Is.EqualTo(tracks.Length));
            foreach (var genre in tracks.Select(track => track.Genre))
            {
                var playlist = playlists.Values.Single(item => item.Name == genre && item.Attribute == 0);
                Assert.That(relations.Count(item => item.PlaylistID == playlist.ID), Is.EqualTo(1));
            }
        }

        [Test]
        public void PlaylistDryRunMutatesNothingAndWriteFailureRollsBackWholeTrack()
        {
            var dryRunDocument = CreateProgressDocument("Hot Cues");
            var beforeDryRun = File.ReadAllBytes(databasePath);
            var dryRun = CreateService(dryRun: true).ImportJson(JsonConvert.SerializeObject(dryRunDocument));
            Assert.That(dryRun.Success, Is.True, string.Join(Environment.NewLine, dryRun.Errors));
            Assert.That(dryRun.Changes.Select(change => change.Field), Does.Contain("desired_playlists"));
            Assert.That(File.ReadAllBytes(databasePath), Is.EqualTo(beforeDryRun));

            var staged = CreateService().ImportJson(JsonConvert.SerializeObject(dryRunDocument));
            Assert.That(staged.Success, Is.True, string.Join(Environment.NewLine, staged.Errors));
            using (var database = OpenDatabase())
            {
                database.Execute(
                    "CREATE TRIGGER fail_workflow_playlist BEFORE INSERT ON djmdSongPlaylist " +
                    "BEGIN SELECT RAISE(ABORT, 'forced playlist failure'); END");
            }

            var result = CreateService().ImportJson(JsonConvert.SerializeObject(CreateProgressDocument(null)));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("forced playlist failure"));
            using (var database = OpenDatabase())
            {
                var content = database.Table<Content>().Single(item => item.ID == TargetContentId);
                Assert.That(content.Rating, Is.EqualTo(4));
                var paths = new RekordboxWorkflowRepository(database).GetManagedPlaylistPaths(TargetContentId);
                Assert.That(paths, Does.Contain("Preparation/Hot Cues"));
                Assert.That(paths, Does.Not.Contain("Preparation/READY"));
                Assert.That(GetAssignedStatus(database), Is.EqualTo(new[] { "Hot Cues" }));
            }
        }

        private WorkflowImportDocument CreateProgressDocument(string status)
        {
            if (status == "To Do" || status == "Mood")
            {
                return CreateDocument(
                    status,
                    "Mutex",
                    "Microgainz - Mutex.mp3",
                    includeMood: false,
                    energy: null);
            }
            if (status == "Energy")
            {
                return CreateDocument(
                    status,
                    "Mutex",
                    "Microgainz - Mutex.mp3",
                    includeMood: true,
                    energy: null);
            }
            if (status == "Tags")
                return CreateDocument(status, "Mutex", "Microgainz - Mutex.mp3", includeMood: true, energy: 4);
            if (status == "Hot Cues")
            {
                return CreateDocument(
                    status,
                    "Mutex",
                    "Microgainz - Mutex.mp3",
                    new[] { "House", "Techno" },
                    includeMood: true,
                    energy: 4);
            }

            return CreateDocument(
                null,
                "Mutex",
                "Microgainz - Mutex.mp3",
                new[] { "House", "Techno" },
                includeMood: true,
                energy: 5);
        }

        private WorkflowImportDocument CreateDocument(
            string status,
            string title,
            string file,
            IList<string> genres = null,
            bool includeMood = true,
            int? energy = 5)
        {
            var taxonomy = WorkflowTaxonomy.LoadDefault();
            var isReady = status == null;
            var document = new WorkflowImportDocument
            {
                SchemaVersion = "2.0",
                Track = new WorkflowTrackIdentity
                {
                    Path = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(databasePath),
                        "content",
                        file)),
                    Title = title
                },
                Status = status,
                Mood = includeMood
                    ? new WorkflowMood { Color = "Red", Label = taxonomy.Moods.Single(item => item.Color == "Red").Label }
                    : null,
                Energy = energy,
                MyTags = genres == null
                    ? null
                    : new WorkflowMyTags
                    {
                        Genres = genres,
                        YearOrigin = new[] { "2024" },
                        Situations = new[] { "Main Floor", "Peak Time" }
                    },
                BeatgridVerified = isReady ? true : null,
                QuantizeVerified = isReady ? true : null,
                HotCues = isReady ? ReadyHotCues() : null
            };
            document.DesiredPlaylists = WorkflowPlaylistPlan.BuildExpectedPaths(document, taxonomy);
            return document;
        }

        private static IList<WorkflowHotCue> ReadyHotCues()
        {
            return new List<WorkflowHotCue>
            {
                new() { Slot = "A", Name = "IN-32", Color = "Green", PositionMs = 0, PhraseStartVerified = true },
                new() { Slot = "C", Name = "DROP 1", Color = "Red", PositionMs = 1000, PhraseStartVerified = true },
                new() { Slot = "E", Name = "OUT-32", Color = "Orange", PositionMs = 2000, PhraseStartVerified = true }
            };
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

        private static IList<string> GetAssignedStatus(SQLiteConnection database)
        {
            var root = database.Table<MyTag>().Single(tag => tag.Name == "Status" && tag.ParentId == "root");
            var children = database.Table<MyTag>()
                .Where(tag => tag.ParentId == root.ID)
                .ToDictionary(tag => tag.ID, tag => tag.Name);
            return database.Table<SongMyTag>()
                .Where(relation => relation.ContentID == TargetContentId)
                .ToList()
                .Where(relation => children.ContainsKey(relation.MyTagID))
                .Select(relation => children[relation.MyTagID])
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
