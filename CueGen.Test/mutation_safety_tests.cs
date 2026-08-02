using System;
using System.IO;
using System.Linq;
using CueGen.Analysis;
using NUnit.Framework;

namespace CueGen.Test
{
    [TestFixture]
    public class MutationSafetyTests
    {
        [Test]
        public void VerifiedBackupMatchesSource()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var databasePath = Path.Combine(directory, "fixture.db");
                File.WriteAllBytes(databasePath, new byte[] { 1, 2, 3, 4 });

                var backupPath = RekordboxSafety.CreateVerifiedBackup(databasePath);

                Assert.That(backupPath, Is.Not.EqualTo(databasePath));
                Assert.That(File.Exists(backupPath), Is.True);
                Assert.That(RekordboxSafety.FilesMatch(databasePath, backupPath), Is.True);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void DryRunStemSeparationCreatesNoFilesOrDirectories()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var audioPath = Path.Combine(directory, "track.mp3");
                var outputPath = Path.Combine(directory, "stems");
                File.WriteAllBytes(audioPath, new byte[] { 1 });
                var before = Directory.GetFileSystemEntries(directory).OrderBy(path => path).ToArray();
                var separator = new StemSeparator(outputPath, dryRun: true);

                Assert.That(separator.SeparateStems(audioPath), Is.True);
                Assert.That(Directory.Exists(outputPath), Is.False);
                Assert.That(Directory.GetFileSystemEntries(directory).OrderBy(path => path), Is.EqualTo(before));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void DryRunBeatGridUpdatePreservesAnalysisBytes()
        {
            var testDirectory = TestContext.CurrentContext.TestDirectory;
            var seedDatabasePath = Path.Combine(testDirectory, "test.db");
            var seedConfig = new Config
            {
                DatabasePath = seedDatabasePath,
                UseSqlCipher = false,
                DryRun = true
            };
            var seedContent = new Generator(seedConfig).GetContents()
                .First(content => !string.IsNullOrEmpty(content.AnalysisDataPath));
            var relativeAnalysisPath = seedContent.AnalysisDataPath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var sourceAnalysisPath = Path.Combine(testDirectory, "share", relativeAnalysisPath);
            var directory = CreateTemporaryDirectory();

            try
            {
                var databasePath = Path.Combine(directory, "test.db");
                var analysisPath = Path.Combine(directory, "share", relativeAnalysisPath);
                Directory.CreateDirectory(Path.GetDirectoryName(analysisPath));
                File.Copy(seedDatabasePath, databasePath);
                File.Copy(sourceAnalysisPath, analysisPath);

                var config = new Config
                {
                    DatabasePath = databasePath,
                    UseSqlCipher = false,
                    DryRun = true
                };
                var content = new Generator(config).GetContents()
                    .Single(item => item.ID == seedContent.ID);
                var beats = content.GetBeats(config)
                    .Select(beat => new AnlzBeat
                    {
                        BeatNumber = beat.BeatNumber,
                        Tempo = beat.Tempo,
                        Time = beat.Time
                    })
                    .ToList();
                Assert.That(beats, Is.Not.Empty);
                beats[0].Time++;
                var before = File.ReadAllBytes(analysisPath);

                content.SetBeats(beats, config);

                Assert.That(File.ReadAllBytes(analysisPath), Is.EqualTo(before));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "rekordbot-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
