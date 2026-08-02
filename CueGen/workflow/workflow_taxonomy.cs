using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CueGen.Workflow
{
    public sealed class WorkflowTaxonomy
    {
        [JsonProperty("schema_version", Required = Required.Always)]
        public string SchemaVersion { get; set; }

        [JsonProperty("categories", Required = Required.Always)]
        public WorkflowTagCategories Categories { get; set; }

        [JsonProperty("statuses", Required = Required.Always)]
        public IList<string> Statuses { get; set; }

        [JsonProperty("moods", Required = Required.Always)]
        public IList<WorkflowMoodMapping> Moods { get; set; }

        [JsonProperty("genres", Required = Required.Always)]
        public IList<string> Genres { get; set; }

        [JsonProperty("year_origin_patterns", Required = Required.Always)]
        public IList<string> YearOriginPatterns { get; set; }

        [JsonProperty("situations", Required = Required.Always)]
        public IList<string> Situations { get; set; }

        [JsonProperty("hot_cues", Required = Required.Always)]
        public IDictionary<string, WorkflowHotCueMapping> HotCues { get; set; }

        public static WorkflowTaxonomy Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A taxonomy path is required", nameof(path));

            return Parse(File.ReadAllText(Path.GetFullPath(path)));
        }

        public static WorkflowTaxonomy LoadDefault()
        {
            var assembly = typeof(WorkflowTaxonomy).GetTypeInfo().Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith("workflow_taxonomy_v2.json", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("The embedded workflow taxonomy is missing");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        private static WorkflowTaxonomy Parse(string json)
        {
            var taxonomy = JsonConvert.DeserializeObject<WorkflowTaxonomy>(json, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error
            }) ?? throw new JsonException("The workflow taxonomy could not be parsed");

            if (!string.Equals(taxonomy.SchemaVersion, "2.0", StringComparison.Ordinal))
                throw new JsonException("The workflow taxonomy must use schema version 2.0");

            return taxonomy;
        }
    }

    public sealed class WorkflowTagCategories
    {
        [JsonProperty("status", Required = Required.Always)]
        public string Status { get; set; }

        [JsonProperty("genres", Required = Required.Always)]
        public string Genres { get; set; }

        [JsonProperty("year_origin", Required = Required.Always)]
        public string YearOrigin { get; set; }

        [JsonProperty("situations", Required = Required.Always)]
        public string Situations { get; set; }
    }

    public sealed class WorkflowMoodMapping
    {
        [JsonProperty("color", Required = Required.Always)]
        public string Color { get; set; }

        [JsonProperty("label", Required = Required.Always)]
        public string Label { get; set; }

        [JsonProperty("color_id", Required = Required.Always)]
        public string ColorId { get; set; }
    }

    public sealed class WorkflowHotCueMapping
    {
        [JsonProperty("name", Required = Newtonsoft.Json.Required.Always)]
        public string Name { get; set; }

        [JsonProperty("color", Required = Newtonsoft.Json.Required.Always)]
        public string Color { get; set; }

        [JsonProperty("color_table_index", Required = Newtonsoft.Json.Required.Always)]
        public int ColorTableIndex { get; set; }

        [JsonProperty("required", Required = Newtonsoft.Json.Required.Always)]
        public bool Required { get; set; }
    }
}
