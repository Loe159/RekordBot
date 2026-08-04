using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CueGen.Workflow
{
    public sealed class WorkflowImportValidator
    {
        private readonly WorkflowTaxonomy taxonomy;

        public WorkflowImportValidator(WorkflowTaxonomy taxonomy)
        {
            this.taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
        }

        public IList<string> Validate(WorkflowImportDocument document)
        {
            var errors = new List<string>();
            if (document == null)
            {
                errors.Add("The import document is required");
                return errors;
            }

            if (!string.Equals(document.SchemaVersion, "2.0", StringComparison.Ordinal))
                errors.Add("schema_version must be exactly 2.0");

            ValidateTrack(document.Track, errors);
            ValidateStatus(document.Status, errors);
            ValidateMood(document.Mood, errors);
            ValidateEnergy(document.Energy, errors);
            ValidateMyTags(document.MyTags, errors);
            ValidateHotCues(document.HotCues, errors);
            ValidateProgressiveRequirements(document, errors);
            ValidateDesiredPlaylists(document, errors);
            return errors;
        }

        private static void ValidateTrack(WorkflowTrackIdentity track, ICollection<string> errors)
        {
            if (track == null)
            {
                errors.Add("track is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(track.Path))
                errors.Add("track.path is required");

            if (string.IsNullOrWhiteSpace(track.Isrc) &&
                string.IsNullOrWhiteSpace(track.Title) &&
                string.IsNullOrWhiteSpace(track.Artist))
            {
                errors.Add("track must provide at least one verifier: isrc, title, or artist");
            }
        }

        private void ValidateStatus(string status, ICollection<string> errors)
        {
            if (status == null)
                return;

            if (!taxonomy.Statuses.Contains(status, StringComparer.Ordinal))
                errors.Add($"Unknown status '{status}'");
        }

        private void ValidateMood(WorkflowMood mood, ICollection<string> errors)
        {
            if (mood == null)
                return;

            var match = taxonomy.Moods.SingleOrDefault(candidate =>
                string.Equals(candidate.Color, mood.Color, StringComparison.Ordinal) &&
                string.Equals(candidate.Label, mood.Label, StringComparison.Ordinal));
            if (match == null)
                errors.Add($"Unknown mood mapping '{mood.Color}/{mood.Label}'");
        }

        private static void ValidateEnergy(int? energy, ICollection<string> errors)
        {
            if (energy.HasValue && (energy.Value < 1 || energy.Value > 5))
                errors.Add("energy must be an integer from 1 to 5");
        }

        private void ValidateMyTags(WorkflowMyTags tags, ICollection<string> errors)
        {
            if (tags == null)
                return;

            ValidateList("my_tags.genres", tags.Genres, taxonomy.Genres, errors);
            ValidatePatternList("my_tags.year_origin", tags.YearOrigin, taxonomy.YearOriginPatterns, errors);
            ValidateList("my_tags.situations", tags.Situations, taxonomy.Situations, errors);
        }

        private static void ValidateList(
            string field,
            IList<string> values,
            IEnumerable<string> allowed,
            ICollection<string> errors)
        {
            if (values == null)
            {
                errors.Add($"{field} is required when my_tags is present");
                return;
            }

            ValidateUnique(field, values, errors);
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (var value in values.Where(value => !allowedSet.Contains(value)))
                errors.Add($"Unknown {field} value '{value}'");
        }

        private static void ValidatePatternList(
            string field,
            IList<string> values,
            IEnumerable<string> patterns,
            ICollection<string> errors)
        {
            if (values == null)
            {
                errors.Add($"{field} is required when my_tags is present");
                return;
            }

            ValidateUnique(field, values, errors);
            var expressions = patterns.Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant)).ToList();
            foreach (var value in values.Where(value => expressions.All(expression => !expression.IsMatch(value))))
                errors.Add($"Unknown {field} value '{value}'");
        }

        private static void ValidateUnique(string field, IList<string> values, ICollection<string> errors)
        {
            if (values.Any(string.IsNullOrWhiteSpace))
                errors.Add($"{field} cannot contain an empty value");

            if (values.Count != values.Distinct(StringComparer.Ordinal).Count())
                errors.Add($"{field} values must be unique");
        }

        private void ValidateHotCues(IList<WorkflowHotCue> hotCues, ICollection<string> errors)
        {
            if (hotCues == null)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cue in hotCues)
            {
                if (cue == null)
                {
                    errors.Add("hot_cues cannot contain null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cue.Slot) || !taxonomy.HotCues.TryGetValue(cue.Slot, out var mapping))
                {
                    errors.Add($"Unknown hot_cues slot '{cue.Slot}'");
                    continue;
                }

                if (!seen.Add(cue.Slot))
                    errors.Add($"hot_cues slot '{cue.Slot}' must be unique");

                if (!mapping.AcceptsName(cue.Name))
                {
                    var allowedNames = new[] { mapping.Name }
                        .Concat(mapping.AlternateNames ?? Array.Empty<string>());
                    errors.Add($"Hot Cue {cue.Slot} must be named one of: {string.Join(", ", allowedNames)}");
                }
                if (!string.Equals(cue.Color, mapping.Color, StringComparison.Ordinal))
                    errors.Add($"Hot Cue {cue.Slot} must use color '{mapping.Color}'");
                if (!cue.PositionMs.HasValue || cue.PositionMs.Value < 0)
                    errors.Add($"Hot Cue {cue.Slot} position_ms must be non-negative");
                if (cue.Slot != "B" && cue.Slot != "C" && cue.PhraseStartVerified != true)
                    errors.Add($"Hot Cue {cue.Slot} must be verified on the first beat of a phrase");
                if (cue.Slot == "B" && cue.VocalSectionVerified != true)
                    errors.Add("Hot Cue B must be verified as the start of an audible four-beat vocal section");
                if (cue.Slot != "B" && cue.VocalSectionVerified == true)
                    errors.Add($"Hot Cue {cue.Slot} cannot be verified as a vocal section");
                if (cue.Slot == "C" && cue.DropOffsetBeats != 32)
                    errors.Add("Hot Cue C drop_offset_beats must be exactly 32");
                if (cue.Slot != "C" && cue.DropOffsetBeats.HasValue)
                    errors.Add($"Hot Cue {cue.Slot} cannot define drop_offset_beats");

                if (cue.Slot == "H")
                {
                    if (cue.LoopBeats != 8 && cue.LoopBeats != 16)
                        errors.Add("Hot Cue H must define an 8- or 16-beat loop");
                }
                else if (cue.LoopBeats.HasValue)
                {
                    errors.Add($"Hot Cue {cue.Slot} cannot define loop_beats");
                }
            }
        }

        private void ValidateProgressiveRequirements(WorkflowImportDocument document, ICollection<string> errors)
        {
            if (document.Status == null)
            {
                if (document.Mood == null)
                    errors.Add("mood is required for READY");
                if (!document.Energy.HasValue)
                    errors.Add("energy is required for READY");
                if (document.MyTags == null)
                    errors.Add("my_tags is required for READY");
                else if (document.MyTags.Genres == null || document.MyTags.Genres.Count == 0)
                    errors.Add("At least one genre is required for READY");
                if (document.BeatgridVerified != true)
                    errors.Add("beatgrid_verified must be true for READY");
                if (document.QuantizeVerified != true)
                    errors.Add("quantize_verified must be true for READY");
                if (document.HotCues == null)
                {
                    errors.Add("hot_cues is required for READY");
                }
                else
                {
                    var present = new HashSet<string>(
                        document.HotCues.Where(cue => cue != null).Select(cue => cue.Slot),
                        StringComparer.Ordinal);
                    var missing = taxonomy.HotCues
                        .Where(pair => pair.Value.Required && !present.Contains(pair.Key))
                        .Select(pair => pair.Key)
                        .OrderBy(slot => slot, StringComparer.Ordinal)
                        .ToList();
                    if (missing.Count > 0)
                        errors.Add($"READY requires Hot Cues {string.Join(", ", missing)}");
                }

                return;
            }

            if (document.Status == "Energy" || document.Status == "Tags" || document.Status == "Hot Cues")
            {
                if (document.Mood == null)
                    errors.Add($"mood is required when status is {document.Status}");
            }

            if (document.Status == "Tags" || document.Status == "Hot Cues")
            {
                if (!document.Energy.HasValue)
                    errors.Add($"energy is required when status is {document.Status}");
            }

            if (document.Status == "Hot Cues")
            {
                if (document.MyTags == null)
                    errors.Add("my_tags is required when status is Hot Cues");
                else if (document.MyTags.Genres == null || document.MyTags.Genres.Count == 0)
                    errors.Add("At least one genre is required when status is Hot Cues");
            }
        }

        private void ValidateDesiredPlaylists(
            WorkflowImportDocument document,
            ICollection<string> errors)
        {
            if (document.DesiredPlaylists == null)
                return;

            ValidateUnique("desired_playlists", document.DesiredPlaylists, errors);
            foreach (var path in document.DesiredPlaylists.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var segments = WorkflowPlaylistPlan.Split(path);
                if (segments.Length != 2 ||
                    segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..") ||
                    path.Contains('\\'))
                {
                    errors.Add($"Invalid desired_playlists path '{path}'");
                }
            }

            var expected = WorkflowPlaylistPlan.BuildExpectedPaths(document, taxonomy);
            var desired = document.DesiredPlaylists
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var missing = expected.Except(desired, StringComparer.Ordinal).ToList();
            var unexpected = desired.Except(expected, StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
                errors.Add($"desired_playlists is missing: {string.Join(", ", missing)}");
            if (unexpected.Count > 0)
                errors.Add($"desired_playlists contains unexpected paths: {string.Join(", ", unexpected)}");
        }
    }
}
