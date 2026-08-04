using Ganss.IO;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CueGen.Workflow
{
    public sealed class WorkflowHotCueGenerationService
    {
        private readonly Config config;
        private readonly WorkflowTaxonomy taxonomy;
        private readonly WorkflowPhraseAnalysisReader analysisReader = new();
        private readonly WorkflowHotCueRuleEngine ruleEngine = new();
        private readonly WorkflowMemoryCueRuleEngine memoryCueRuleEngine = new();
        private readonly WorkflowImportValidator validator;
        private readonly WorkflowImportService importService;

        public WorkflowHotCueGenerationService(Config config, WorkflowTaxonomy taxonomy = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.taxonomy = taxonomy ?? WorkflowTaxonomy.LoadDefault();
            validator = new WorkflowImportValidator(this.taxonomy);
            importService = new WorkflowImportService(config, this.taxonomy);
        }

        public WorkflowHotCueBatchResult Generate()
        {
            var result = new WorkflowHotCueBatchResult { DryRun = config.DryRun };
            if (string.IsNullOrWhiteSpace(config.FileGlob))
            {
                result.Errors.Add("A file glob is required for workflow Hot Cue generation");
                return result;
            }

            try
            {
                RekordboxSafety.ValidateDatabase(config.DatabasePath);
                using var database = new SQLiteConnection(new Generator(config).ConnectionString);
                var glob = new Glob(config.FileGlob, new GlobOptions { IgnoreCase = true });
                var contents = database.Table<Content>().ToList();
                var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? string.Empty;
                var selected = contents
                    .Where(content =>
                        !string.IsNullOrWhiteSpace(content.FolderPath) &&
                        !IsStemPath(content.FolderPath) &&
                        glob.IsMatch(content.FolderPath))
                    .OrderBy(content => content.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (config.SeparateStems)
                {
                    var stemWarnings = EnsureVocalStems(selected, databaseDirectory);
                    foreach (var warning in stemWarnings)
                        result.Errors.Add(warning);
                }

                var contentsByPath = contents
                    .Where(content => !string.IsNullOrWhiteSpace(content.FolderPath))
                    .GroupBy(content => NormalizePath(content.FolderPath, databaseDirectory), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToList(),
                        StringComparer.OrdinalIgnoreCase);
                result.SelectedCount = selected.Count;
                if (selected.Count == 0)
                {
                    result.Errors.Add($"No Rekordbox track matches glob '{config.FileGlob}'");
                    return result;
                }

                foreach (var content in selected)
                    result.Tracks.Add(ProcessTrack(database, content, contentsByPath));

                result.Success = result.Tracks.All(track => track.Success);
                return result;
            }
            catch (Exception exception)
            {
                result.Errors.Add(exception.Message);
                return result;
            }
        }

        private WorkflowHotCueTrackResult ProcessTrack(
            SQLiteConnection database,
            Content content,
            IReadOnlyDictionary<string, List<Content>> contentsByPath)
        {
            var result = new WorkflowHotCueTrackResult
            {
                ContentId = content.ID,
                Path = content.FolderPath
            };

            try
            {
                var repository = new RekordboxWorkflowRepository(database);
                var timeline = analysisReader.Read(content, config);
                var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath)) ?? string.Empty;
                var vocalWaveformWarning = AttachVocalWaveform(content, timeline, contentsByPath, databaseDirectory);
                var proposal = ruleEngine.Generate(timeline, taxonomy);
                if (vocalWaveformWarning != null)
                    proposal.Warnings.Add(vocalWaveformWarning);
                var memoryCueProposal = memoryCueRuleEngine.Generate(
                    timeline,
                    repository.GetAllMemoryCueStates(content.ID),
                    content.Length.HasValue ? content.Length.Value * 1000 : (int?)null);
                result.HotCues = proposal.HotCues;
                result.Evidence = proposal.Evidence;
                result.Warnings = proposal.Warnings.ToList();
                result.MemoryCues = memoryCueProposal.MemoryCues;
                foreach (var warning in memoryCueProposal.Warnings)
                    result.Warnings.Add(warning);

                var document = BuildDocument(repository, content, proposal, result.Warnings);
                result.Status = document.Status;
                result.Ready = document.Status == null;

                var validationErrors = validator.Validate(document);
                if (validationErrors.Count > 0)
                    throw new InvalidOperationException(string.Join("; ", validationErrors));

                repository.ValidateHotCuePreflight(content, document.HotCues);
                repository.ValidateMemoryCuePreflight(content, memoryCueProposal.MemoryCues);
                repository.ValidatePlaylistPreflight(document.DesiredPlaylists);
                result.Changes = importService.BuildChanges(repository, content, document);
                AddMemoryCueChange(result.Changes, repository, content.ID, memoryCueProposal.MemoryCues);
                var reviewDisposition = importService.FinalizeChangesForReview(
                    repository,
                    content.ID,
                    result.Changes);
                if (reviewDisposition != WorkflowReviewDisposition.NotRequired)
                {
                    result.Status = WorkflowImportService.ReviewStatus;
                    result.Ready = false;
                }

                if (!config.DryRun && result.Changes.Count > 0)
                {
                    database.RunInTransaction(() =>
                    {
                        importService.Apply(repository, content, document, reviewDisposition);
                        repository.SyncMemoryCues(content, memoryCueProposal.MemoryCues);
                    });
                }

                result.Success = true;
            }
            catch (Exception exception)
            {
                result.Errors.Add(exception.Message);
                result.Ready = false;
            }

            return result;
        }

        private string AttachVocalWaveform(
            Content content,
            WorkflowPhraseTimeline timeline,
            IReadOnlyDictionary<string, List<Content>> contentsByPath,
            string databaseDirectory)
        {
            var expectedPath = GetVocalStemPath(content.FolderPath);
            if (!contentsByPath.TryGetValue(NormalizePath(expectedPath, databaseDirectory), out var matches))
            {
                var exists = File.Exists(NormalizePath(expectedPath, databaseDirectory));
                return exists
                    ? $"B missing: vocal stem '{expectedPath}' exists but is not an analyzed Rekordbox content row"
                    : $"B missing: no analyzed vocal stem content row was found for '{expectedPath}'";
            }
            if (matches.Count > 1)
                return $"B missing: multiple analyzed vocal stems match '{expectedPath}'";

            try
            {
                timeline.VocalWaveformHeights = analysisReader.ReadWaveformHeights(matches[0], config);
                return timeline.VocalWaveformHeights.Count == 0
                    ? $"B missing: the vocal stem '{expectedPath}' has no detailed waveform analysis"
                    : null;
            }
            catch (Exception exception)
            {
                timeline.VocalWaveformHeights = new List<byte>();
                return $"B missing: the vocal stem waveform could not be read: {exception.Message}";
            }
        }

        private IList<string> EnsureVocalStems(
            IEnumerable<Content> selected,
            string databaseDirectory)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(config.StemsOutputDirectory))
            {
                warnings.Add("Stem separation was requested but StemsOutputDirectory is not configured");
                return warnings;
            }

            var separator = new StemSeparator(
                config.StemsOutputDirectory,
                config.DemucsCommand,
                config.DryRun);
            foreach (var content in selected)
            {
                var sourcePath = NormalizePath(content.FolderPath, databaseDirectory)
                    .Replace('/', Path.DirectorySeparatorChar);
                var vocalPath = GetVocalStemPath(sourcePath);
                if (File.Exists(vocalPath))
                    continue;

                if (!separator.SeparateStems(sourcePath, config.DemucsModel))
                {
                    warnings.Add($"Vocal stem creation failed for '{content.FolderPath}'");
                    continue;
                }

                if (!config.DryRun && !File.Exists(vocalPath))
                    warnings.Add($"Vocal stem creation did not produce '{vocalPath}'");
            }

            return warnings;
        }

        private static string GetVocalStemPath(string sourcePath)
        {
            var directory = System.IO.Path.GetDirectoryName(sourcePath);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            var extension = string.Equals(
                System.IO.Path.GetExtension(sourcePath),
                ".flac",
                StringComparison.OrdinalIgnoreCase)
                ? ".flac"
                : ".mp3";
            return System.IO.Path.Combine(directory ?? string.Empty, $"{fileName}_vocal{extension}");
        }

        private static string NormalizePath(string path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = path.Replace('\\', Path.DirectorySeparatorChar);
            var combined = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(baseDirectory ?? string.Empty, normalized);
            return Path.GetFullPath(combined)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static bool IsStemPath(string path)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            return fileName.EndsWith("_vocal", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("_instrumental", StringComparison.OrdinalIgnoreCase);
        }

        private WorkflowImportDocument BuildDocument(
            RekordboxWorkflowRepository repository,
            Content content,
            WorkflowHotCueProposal proposal,
            ICollection<string> warnings)
        {
            var moodMapping = taxonomy.Moods.SingleOrDefault(mapping =>
                string.Equals(mapping.ColorId, content.ColorID, StringComparison.Ordinal));
            var mood = moodMapping == null
                ? null
                : new WorkflowMood { Color = moodMapping.Color, Label = moodMapping.Label };
            if (mood == null)
                warnings.Add("mood_missing_or_invalid");

            var energy = content.Rating is >= 1 and <= 5 ? content.Rating : null;
            if (!energy.HasValue)
                warnings.Add("energy_missing_or_invalid");

            var assignedGenres = repository.GetAssignedTagNames(content.ID, taxonomy.Categories.Genres);
            var assignedYearOrigin = repository.GetAssignedTagNames(content.ID, taxonomy.Categories.YearOrigin);
            var assignedSituations = repository.GetAssignedTagNames(content.ID, taxonomy.Categories.Situations);
            var genres = FilterKnown(assignedGenres, taxonomy.Genres);
            var yearOrigin = FilterPatterns(assignedYearOrigin, taxonomy.YearOriginPatterns);
            var situations = FilterKnown(assignedSituations, taxonomy.Situations);
            var tagsComplete = genres.Count > 0 &&
                genres.Count == assignedGenres.Count &&
                yearOrigin.Count == assignedYearOrigin.Count &&
                situations.Count == assignedSituations.Count;
            if (!tagsComplete)
                warnings.Add("tags_missing_or_invalid");

            var quantizeVerified = content.DisableQuantize == 0;
            if (!quantizeVerified)
                warnings.Add("quantize_unverified");

            var status = mood == null
                ? "Mood"
                : !energy.HasValue
                    ? "Energy"
                    : !tagsComplete
                        ? "Tags"
                        : !proposal.Complete || !quantizeVerified
                            ? "Hot Cues"
                            : null;

            var document = new WorkflowImportDocument
            {
                SchemaVersion = "2.0",
                Track = new WorkflowTrackIdentity
                {
                    Path = content.FolderPath,
                    Isrc = content.ISRC,
                    Title = content.Title ?? content.FileNameL ?? content.ID
                },
                Status = status,
                Mood = mood,
                Energy = energy,
                MyTags = new WorkflowMyTags
                {
                    Genres = genres,
                    YearOrigin = yearOrigin,
                    Situations = situations
                },
                BeatgridVerified = true,
                QuantizeVerified = quantizeVerified,
                HotCues = proposal.HotCues
            };
            document.DesiredPlaylists = WorkflowPlaylistPlan.BuildExpectedPaths(document, taxonomy);
            return document;
        }

        private static void AddMemoryCueChange(
            ICollection<WorkflowImportChange> changes,
            RekordboxWorkflowRepository repository,
            string contentId,
            IList<WorkflowMemoryCue> desiredCues)
        {
            var before = repository.GetWorkflowMemoryCueStates(contentId);
            var after = desiredCues
                .Select(cue => new WorkflowMemoryCueState
                {
                    Name = cue.Name,
                    PositionMs = cue.PositionMs,
                    LoopBeats = cue.LoopBeats,
                    LoopEndMs = cue.LoopEndMs,
                    Managed = true
                })
                .OrderBy(cue => cue.PositionMs)
                .ThenBy(cue => cue.Name, StringComparer.Ordinal)
                .ToList();
            if (!MemoryCueStatesEqual(before, after) || !repository.IsContentCueConsistent(contentId))
            {
                changes.Add(new WorkflowImportChange
                {
                    Field = "memory_cues",
                    Before = before,
                    After = after
                });
            }
        }

        private static bool MemoryCueStatesEqual(
            IList<WorkflowMemoryCueState> before,
            IList<WorkflowMemoryCueState> after)
        {
            return before.Count == after.Count && before.Zip(after, (left, right) =>
                left.Name == right.Name &&
                left.PositionMs == right.PositionMs &&
                left.LoopBeats == right.LoopBeats &&
                left.LoopEndMs == right.LoopEndMs).All(equal => equal);
        }

        private static IList<string> FilterKnown(IEnumerable<string> assigned, IEnumerable<string> allowed)
        {
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            return assigned
                .Where(allowedSet.Contains)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private static IList<string> FilterPatterns(IEnumerable<string> assigned, IEnumerable<string> patterns)
        {
            var expressions = patterns
                .Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant))
                .ToList();
            return assigned
                .Where(value => expressions.Any(expression => expression.IsMatch(value)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }
    }
}
