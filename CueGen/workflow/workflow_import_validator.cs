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
            ValidateProgressiveRequirements(document, errors);
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
            {
                errors.Add("status cannot be null until the phase 3 READY gate is implemented");
                return;
            }

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

        private static void ValidateProgressiveRequirements(WorkflowImportDocument document, ICollection<string> errors)
        {
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
    }
}
