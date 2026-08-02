using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CueGen
{
    public class SoundchartsResponse<T>
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("object")]
        public T Object { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }
    }

    public class SoundchartsSong
    {
        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("isrc")]
        public SoundchartsIsrc Isrc { get; set; }

        [JsonProperty("creditName")]
        public string CreditName { get; set; }

        [JsonProperty("artists")]
        public List<SoundchartsArtist> Artists { get; set; }

        [JsonProperty("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonProperty("duration")]
        public int? Duration { get; set; }

        [JsonProperty("audio")]
        public SoundchartsAudio Audio { get; set; }

        [JsonProperty("genres")]
        public List<SoundchartsGenre> Genres { get; set; }

        [JsonProperty("labels")]
        public List<SoundchartsLabel> Labels { get; set; }
    }

    public class SoundchartsIsrc
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        [JsonProperty("countryName")]
        public string CountryName { get; set; }
    }

    public class SoundchartsArtist
    {
        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }
    }

    public class SoundchartsAudio
    {
        [JsonProperty("acousticness")]
        public float? Acousticness { get; set; }

        [JsonProperty("danceability")]
        public float? Danceability { get; set; }

        [JsonProperty("energy")]
        public float? Energy { get; set; }

        [JsonProperty("instrumentalness")]
        public float? Instrumentalness { get; set; }

        [JsonProperty("key")]
        public int? Key { get; set; }

        [JsonProperty("liveness")]
        public float? Liveness { get; set; }

        [JsonProperty("loudness")]
        public float? Loudness { get; set; }

        [JsonProperty("mode")]
        public int? Mode { get; set; }

        [JsonProperty("speechiness")]
        public float? Speechiness { get; set; }

        [JsonProperty("tempo")]
        public float? Tempo { get; set; }

        [JsonProperty("timeSignature")]
        public int? TimeSignature { get; set; }

        [JsonProperty("valence")]
        public float? Valence { get; set; }
    }

    public class SoundchartsGenre
    {
        [JsonProperty("root")]
        public string Root { get; set; }

        [JsonProperty("sub")]
        public List<string> Sub { get; set; }
    }

    public class SoundchartsLabel
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }
}
