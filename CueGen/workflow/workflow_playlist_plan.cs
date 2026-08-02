using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CueGen.Workflow
{
    public static class WorkflowPlaylistPlan
    {
        public const string RootName = "RekordBot";
        public const string PreparationFolder = "Preparation";
        public const string ReadyName = "READY";

        public static IList<string> BuildExpectedPaths(
            WorkflowImportDocument document,
            WorkflowTaxonomy taxonomy)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (taxonomy == null)
                throw new ArgumentNullException(nameof(taxonomy));

            var paths = new List<string>
            {
                Join(PreparationFolder, document.Status ?? ReadyName)
            };
            if (document.Mood != null)
                paths.Add(Join("Mood", document.Mood.Label));
            if (document.Energy.HasValue)
                paths.Add(Join("Energy", document.Energy.Value.ToString(CultureInfo.InvariantCulture)));
            if (document.MyTags != null)
            {
                paths.AddRange((document.MyTags.Genres ?? Array.Empty<string>())
                    .Select(value => Join(taxonomy.Categories.Genres, value)));
                paths.AddRange((document.MyTags.YearOrigin ?? Array.Empty<string>())
                    .Select(value => Join(taxonomy.Categories.YearOrigin, value)));
                paths.AddRange((document.MyTags.Situations ?? Array.Empty<string>())
                    .Select(value => Join(taxonomy.Categories.Situations, value)));
            }

            return paths
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        public static string[] Split(string path)
        {
            return path?.Split('/') ?? Array.Empty<string>();
        }

        private static string Join(string folder, string name)
        {
            return $"{folder}/{name}";
        }
    }
}
