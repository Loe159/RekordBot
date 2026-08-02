using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CueGen.Workflow
{
    public sealed class WorkflowImportService
    {
        private readonly Config config;
        private readonly WorkflowTaxonomy taxonomy;
        private readonly WorkflowImportValidator validator;
        private readonly WorkflowTrackResolver resolver = new();

        public WorkflowImportService(Config config, WorkflowTaxonomy taxonomy = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.taxonomy = taxonomy ?? WorkflowTaxonomy.LoadDefault();
            validator = new WorkflowImportValidator(this.taxonomy);
        }

        public WorkflowImportResult ImportFile(string importPath)
        {
            if (string.IsNullOrWhiteSpace(importPath))
                return Failure("An import document path is required");

            try
            {
                return ImportJson(File.ReadAllText(Path.GetFullPath(importPath)));
            }
            catch (Exception exception)
            {
                return Failure(exception.Message);
            }
        }

        public WorkflowImportResult ImportJson(string json)
        {
            WorkflowImportDocument document;
            try
            {
                document = WorkflowImportParser.Parse(json);
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                return Failure(exception.Message);
            }

            var validationErrors = validator.Validate(document);
            if (validationErrors.Count > 0)
                return Failure(validationErrors);

            try
            {
                var databasePath = RekordboxSafety.ValidateDatabase(config.DatabasePath);
                using var database = new SQLiteConnection(new Generator(config).ConnectionString);
                var repository = new RekordboxWorkflowRepository(database);
                var artists = string.IsNullOrWhiteSpace(document.Track.Artist)
                    ? new List<Artist>()
                    : repository.GetArtists();
                var content = resolver.Resolve(
                    document.Track,
                    repository.GetContents(),
                    artists,
                    databasePath);
                repository.ValidateHotCuePreflight(content, document.HotCues);
                var changes = BuildChanges(repository, content, document);

                if (!config.DryRun && changes.Count > 0)
                {
                    database.RunInTransaction(() => Apply(repository, content, document));
                }

                return new WorkflowImportResult
                {
                    Success = true,
                    DryRun = config.DryRun,
                    ContentId = content.ID,
                    Changes = changes
                };
            }
            catch (Exception exception)
            {
                return Failure(exception.Message);
            }
        }

        private IList<WorkflowImportChange> BuildChanges(
            RekordboxWorkflowRepository repository,
            Content content,
            WorkflowImportDocument document)
        {
            var changes = new List<WorkflowImportChange>();
            if (document.Mood != null)
            {
                var colorId = GetMood(document.Mood).ColorId;
                AddScalarChange(changes, "mood.color_id", content.ColorID, colorId);
            }

            if (document.Energy.HasValue)
                AddScalarChange(changes, "energy", content.Rating, document.Energy.Value);

            AddTagChange(
                changes,
                repository,
                content.ID,
                "status",
                taxonomy.Categories.Status,
                DesiredStatus(document.Status));

            if (document.MyTags != null)
            {
                AddTagChange(changes, repository, content.ID, "my_tags.genres", taxonomy.Categories.Genres, document.MyTags.Genres);
                AddTagChange(changes, repository, content.ID, "my_tags.year_origin", taxonomy.Categories.YearOrigin, document.MyTags.YearOrigin);
                AddTagChange(changes, repository, content.ID, "my_tags.situations", taxonomy.Categories.Situations, document.MyTags.Situations);
            }

            AddHotCueChange(changes, repository, content.ID, document.HotCues);

            return changes;
        }

        private void Apply(
            RekordboxWorkflowRepository repository,
            Content content,
            WorkflowImportDocument document)
        {
            var colorId = document.Mood == null ? null : GetMood(document.Mood).ColorId;
            repository.UpdateMetadata(content, colorId, document.Energy);
            repository.SyncCategory(content.ID, taxonomy.Categories.Status, DesiredStatus(document.Status));

            if (document.MyTags != null)
            {
                repository.SyncCategory(content.ID, taxonomy.Categories.Genres, document.MyTags.Genres);
                repository.SyncCategory(content.ID, taxonomy.Categories.YearOrigin, document.MyTags.YearOrigin);
                repository.SyncCategory(content.ID, taxonomy.Categories.Situations, document.MyTags.Situations);
            }

            repository.SyncHotCues(content, document.HotCues, taxonomy);
        }

        private WorkflowMoodMapping GetMood(WorkflowMood mood)
        {
            return taxonomy.Moods.Single(mapping =>
                mapping.Color == mood.Color && mapping.Label == mood.Label);
        }

        private static void AddScalarChange(
            ICollection<WorkflowImportChange> changes,
            string field,
            object before,
            object after)
        {
            if (!Equals(before, after))
                changes.Add(new WorkflowImportChange { Field = field, Before = before, After = after });
        }

        private static void AddTagChange(
            ICollection<WorkflowImportChange> changes,
            RekordboxWorkflowRepository repository,
            string contentId,
            string field,
            string category,
            IEnumerable<string> desiredNames)
        {
            var before = repository.GetAssignedTagNames(contentId, category)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var after = desiredNames
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (!before.SequenceEqual(after, StringComparer.Ordinal))
                changes.Add(new WorkflowImportChange { Field = field, Before = before, After = after });
        }

        private void AddHotCueChange(
            ICollection<WorkflowImportChange> changes,
            RekordboxWorkflowRepository repository,
            string contentId,
            IList<WorkflowHotCue> desiredCues)
        {
            if (desiredCues == null)
                return;

            var before = repository.GetManagedHotCueStates(contentId, taxonomy);
            var after = desiredCues
                .Select(cue =>
                {
                    var mapping = taxonomy.HotCues[cue.Slot];
                    return new WorkflowHotCueState
                    {
                        Slot = cue.Slot,
                        Name = mapping.Name,
                        Color = mapping.Color,
                        ColorTableIndex = mapping.ColorTableIndex,
                        PositionMs = cue.PositionMs.Value,
                        LoopBeats = cue.LoopBeats
                    };
                })
                .OrderBy(cue => cue.Slot, StringComparer.Ordinal)
                .ToList();
            if (!HotCueStatesEqual(before, after) || !repository.IsContentCueConsistent(contentId))
                changes.Add(new WorkflowImportChange { Field = "hot_cues", Before = before, After = after });
        }

        private static bool HotCueStatesEqual(
            IList<WorkflowHotCueState> before,
            IList<WorkflowHotCueState> after)
        {
            return before.Count == after.Count && before.Zip(after, (left, right) =>
                left.Slot == right.Slot &&
                left.Name == right.Name &&
                left.ColorTableIndex == right.ColorTableIndex &&
                left.PositionMs == right.PositionMs &&
                left.LoopBeats == right.LoopBeats).All(equal => equal);
        }

        private static IEnumerable<string> DesiredStatus(string status)
        {
            return status == null ? Enumerable.Empty<string>() : new[] { status };
        }

        private WorkflowImportResult Failure(string error)
        {
            return Failure(new[] { error });
        }

        private WorkflowImportResult Failure(IEnumerable<string> errors)
        {
            return new WorkflowImportResult
            {
                Success = false,
                DryRun = config.DryRun,
                Errors = errors.ToList()
            };
        }
    }
}
