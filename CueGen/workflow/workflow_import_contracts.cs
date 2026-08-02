using Newtonsoft.Json;
using System.Collections.Generic;

namespace CueGen.Workflow
{
    public sealed class WorkflowImportDocument
    {
        [JsonProperty("schema_version", Required = Required.Always)]
        public string SchemaVersion { get; set; }

        [JsonProperty("track", Required = Required.Always)]
        public WorkflowTrackIdentity Track { get; set; }

        [JsonProperty("status", Required = Required.AllowNull)]
        public string Status { get; set; }

        [JsonProperty("mood")]
        public WorkflowMood Mood { get; set; }

        [JsonProperty("energy")]
        public int? Energy { get; set; }

        [JsonProperty("my_tags")]
        public WorkflowMyTags MyTags { get; set; }

        [JsonProperty("beatgrid_verified")]
        public bool? BeatgridVerified { get; set; }

        [JsonProperty("quantize_verified")]
        public bool? QuantizeVerified { get; set; }

        [JsonProperty("hot_cues")]
        public IList<WorkflowHotCue> HotCues { get; set; }
    }

    public sealed class WorkflowTrackIdentity
    {
        [JsonProperty("path", Required = Required.Always)]
        public string Path { get; set; }

        [JsonProperty("isrc")]
        public string Isrc { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }
    }

    public sealed class WorkflowMood
    {
        [JsonProperty("color", Required = Required.Always)]
        public string Color { get; set; }

        [JsonProperty("label", Required = Required.Always)]
        public string Label { get; set; }
    }

    public sealed class WorkflowMyTags
    {
        [JsonProperty("genres", Required = Required.Always)]
        public IList<string> Genres { get; set; }

        [JsonProperty("year_origin", Required = Required.Always)]
        public IList<string> YearOrigin { get; set; }

        [JsonProperty("situations", Required = Required.Always)]
        public IList<string> Situations { get; set; }
    }

    public sealed class WorkflowHotCue
    {
        [JsonProperty("slot", Required = Required.Always)]
        public string Slot { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("color", Required = Required.Always)]
        public string Color { get; set; }

        [JsonProperty("position_ms", Required = Required.Always)]
        public int? PositionMs { get; set; }

        [JsonProperty("phrase_start_verified", Required = Required.Always)]
        public bool? PhraseStartVerified { get; set; }

        [JsonProperty("loop_beats")]
        public int? LoopBeats { get; set; }
    }

    public sealed class WorkflowHotCueState
    {
        public string Slot { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public int? ColorTableIndex { get; set; }
        public int PositionMs { get; set; }
        public int? LoopBeats { get; set; }
    }

    public sealed class WorkflowImportChange
    {
        public string Field { get; set; }
        public object Before { get; set; }
        public object After { get; set; }
    }

    public sealed class WorkflowImportResult
    {
        public bool Success { get; set; }
        public bool DryRun { get; set; }
        public string ContentId { get; set; }
        public IList<WorkflowImportChange> Changes { get; set; } = new List<WorkflowImportChange>();
        public IList<string> Errors { get; set; } = new List<string>();
    }
}
