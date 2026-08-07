using Newtonsoft.Json.Linq;
using SQLite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace CueGen.Workflow
{
    public sealed class AirtableSyncOptions
    {
        public const string TokenVariable = "AIRTABLE_TOKEN";
        public const string BaseIdVariable = "AIRTABLE_BASE_ID";
        public const string TableIdVariable = "AIRTABLE_TABLE_ID";
        public const string ViewVariable = "AIRTABLE_VIEW";
        public const string PendingStatusVariable = "AIRTABLE_PENDING_STATUS";
        public const string ReadyStatusVariable = "AIRTABLE_READY_STATUS";
        public const string StatusFieldVariable = "AIRTABLE_STATUS_FIELD";

        public string Token { get; set; }
        public string BaseId { get; set; }
        public string TableId { get; set; }
        public string View { get; set; }
        public string PendingStatus { get; set; } = "À préparer dans Rekordbox";
        public string ReadyStatus { get; set; } = "Prêt à mixer";
        public string StatusFieldName { get; set; } = "Statut";

        public static AirtableSyncOptions Load(Func<string, string> readEnvironment)
        {
            if (readEnvironment == null)
                throw new ArgumentNullException(nameof(readEnvironment));

            var options = new AirtableSyncOptions
            {
                Token = Clean(readEnvironment(TokenVariable)),
                BaseId = Clean(readEnvironment(BaseIdVariable)),
                TableId = Clean(readEnvironment(TableIdVariable)),
                View = Clean(readEnvironment(ViewVariable))
            };

            var pending = Clean(readEnvironment(PendingStatusVariable));
            if (!string.IsNullOrWhiteSpace(pending))
                options.PendingStatus = pending;

            var ready = Clean(readEnvironment(ReadyStatusVariable));
            if (!string.IsNullOrWhiteSpace(ready))
                options.ReadyStatus = ready;

            var statusField = Clean(readEnvironment(StatusFieldVariable));
            if (!string.IsNullOrWhiteSpace(statusField))
                options.StatusFieldName = statusField;

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Token)) missing.Add(TokenVariable);
            if (string.IsNullOrWhiteSpace(options.BaseId)) missing.Add(BaseIdVariable);
            if (string.IsNullOrWhiteSpace(options.TableId)) missing.Add(TableIdVariable);
            if (missing.Count > 0)
                throw new InvalidOperationException("Missing Airtable configuration: " + string.Join(", ", missing));

            return options;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public sealed class AirtableSyncService
    {
        private readonly Config config;
        private readonly WorkflowTaxonomy taxonomy;
        private readonly AirtableSyncOptions options;
        private readonly AirtableApiClient airtable;

        public AirtableSyncService(Config config, WorkflowTaxonomy taxonomy, AirtableSyncOptions options)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            airtable = new AirtableApiClient(options);
        }

        public AirtableSyncBatchResult Synchronize()
        {
            var batch = new AirtableSyncBatchResult { DryRun = config.DryRun };
            try
            {
                var records = airtable.ListPendingRecords();
                batch.SelectedCount = records.Count;
                var matches = LoadRekordboxMatches();
                var completedRecordIds = new List<string>();

                foreach (var record in records)
                {
                    var trackResult = new AirtableSyncTrackResult
                    {
                        RecordId = record.Id,
                        Title = record.Title,
                        Artist = record.Artist
                    };
                    batch.Tracks.Add(trackResult);

                    try
                    {
                        var match = ResolveTrack(record, matches);
                        trackResult.Path = match.Path;
                        if (!string.IsNullOrWhiteSpace(match.Warning))
                            trackResult.Warnings.Add(match.Warning);

                        var document = BuildDocument(record, match, trackResult.Warnings);
                        document.DesiredPlaylists = WorkflowPlaylistPlan.BuildExpectedPaths(document, taxonomy);
                        var importJson = Newtonsoft.Json.JsonConvert.SerializeObject(document);
                        var importResult = new WorkflowImportService(config, taxonomy).ImportJson(importJson);
                        trackResult.Import = importResult;

                        if (!importResult.Success)
                        {
                            foreach (var error in importResult.Errors)
                                trackResult.Errors.Add(error);
                            continue;
                        }

                        if (!config.DryRun)
                            completedRecordIds.Add(record.Id);
                    }
                    catch (Exception exception)
                    {
                        trackResult.Errors.Add(exception.Message);
                    }
                }

                if (!config.DryRun && completedRecordIds.Count > 0)
                {
                    try
                    {
                        airtable.UpdateStatuses(completedRecordIds, options.ReadyStatus);
                        foreach (var result in batch.Tracks.Where(result => completedRecordIds.Contains(result.RecordId)))
                            result.AirtableStatusUpdated = true;
                    }
                    catch (Exception exception)
                    {
                        batch.Errors.Add("Rekordbox imports succeeded but Airtable status update failed: " + exception.Message);
                    }
                }
            }
            catch (Exception exception)
            {
                batch.Errors.Add(exception.Message);
            }

            batch.Success = batch.Errors.Count == 0 && batch.Tracks.All(track => track.Errors.Count == 0);
            return batch;
        }

        private IList<RekordboxTrackMatch> LoadRekordboxMatches()
        {
            using var database = new SQLiteConnection(new Generator(config).ConnectionString);
            var repository = new RekordboxWorkflowRepository(database);
            var artists = repository.GetArtists()
                .GroupBy(artist => artist.ID, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
            var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath))
                ?? throw new InvalidOperationException("The Rekordbox database directory could not be resolved");

            return repository.GetContents()
                .Where(content => !string.IsNullOrWhiteSpace(content.FolderPath))
                .Select(content =>
                {
                    artists.TryGetValue(content.ArtistID, out var artistName);
                    return new RekordboxTrackMatch
                    {
                        Title = content.Title,
                        Artist = artistName,
                        Path = NormalizeContentPath(content.FolderPath, databaseDirectory)
                    };
                })
                .Where(match => File.Exists(match.Path))
                .ToList();
        }

        private static RekordboxTrackMatch ResolveTrack(AirtableSourceRecord record, IList<RekordboxTrackMatch> matches)
        {
            if (string.IsNullOrWhiteSpace(record.Title))
                throw new InvalidOperationException("Airtable record is missing Titre");

            var normalizedTitle = NormalizeIdentity(record.Title);
            var titleMatches = matches
                .Where(match => NormalizeIdentity(match.Title) == normalizedTitle)
                .ToList();
            if (titleMatches.Count == 0)
                throw new InvalidOperationException($"No Rekordbox track matches Airtable title '{record.Title}'");

            if (!string.IsNullOrWhiteSpace(record.Artist))
            {
                var normalizedArtist = NormalizeIdentity(record.Artist);
                var exact = titleMatches
                    .Where(match => NormalizeIdentity(match.Artist) == normalizedArtist)
                    .ToList();
                if (exact.Count == 1)
                    return exact[0];
                if (exact.Count > 1)
                    throw new InvalidOperationException($"Multiple Rekordbox tracks match '{record.Artist} - {record.Title}'");
            }

            if (titleMatches.Count == 1)
            {
                titleMatches[0].Warning = string.IsNullOrWhiteSpace(record.Artist)
                    ? "Airtable artist is empty; matched the unique Rekordbox title"
                    : $"Airtable artist '{record.Artist}' did not match Rekordbox; used the unique title match '{titleMatches[0].Artist}'";
                return titleMatches[0];
            }

            throw new InvalidOperationException($"Multiple Rekordbox tracks match Airtable title '{record.Title}'; artist verification is required");
        }

        private WorkflowImportDocument BuildDocument(
            AirtableSourceRecord record,
            RekordboxTrackMatch match,
            IList<string> warnings)
        {
            var mood = MapMood(record.Moods, warnings);
            var genres = MapGenres(record.SoundchartsGenre, warnings);
            var situations = MapSituations(record.Situations, warnings);
            var status = mood == null
                ? "Mood"
                : !record.Energy.HasValue
                    ? "Energy"
                    : genres.Count == 0
                        ? "Tags"
                        : "Hot Cues";

            return new WorkflowImportDocument
            {
                SchemaVersion = "2.0",
                Track = new WorkflowTrackIdentity
                {
                    Path = match.Path,
                    Title = match.Title,
                    Artist = match.Artist
                },
                Status = status,
                Mood = mood,
                Energy = record.Energy,
                MyTags = new WorkflowMyTags
                {
                    Genres = genres,
                    YearOrigin = new List<string>(),
                    Situations = situations
                },
                BeatgridVerified = null,
                QuantizeVerified = null,
                HotCues = null
            };
        }

        private WorkflowMood MapMood(IList<string> sourceMoods, IList<string> warnings)
        {
            if (sourceMoods == null || sourceMoods.Count == 0)
                return null;

            if (sourceMoods.Count > 1)
                warnings.Add("Airtable contains several moods; the first one is used as the dominant Rekordbox mood");

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Énergique"] = "Énergie",
                ["Joyeux"] = "Joie",
                ["Mystérieux"] = "Mystère"
            };
            var source = sourceMoods[0];
            var label = aliases.TryGetValue(source, out var alias) ? alias : source;
            var mapping = taxonomy.Moods.FirstOrDefault(candidate =>
                string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase));
            if (mapping == null)
            {
                warnings.Add($"Airtable mood '{source}' is not present in the workflow taxonomy");
                return null;
            }

            return new WorkflowMood { Color = mapping.Color, Label = mapping.Label };
        }

        private IList<string> MapGenres(string rawGenre, IList<string> warnings)
        {
            var mapped = new List<string>();
            if (string.IsNullOrWhiteSpace(rawGenre))
                return mapped;

            void AddAllowed(string value)
            {
                var allowed = taxonomy.Genres.FirstOrDefault(genre =>
                    string.Equals(genre, value, StringComparison.OrdinalIgnoreCase));
                if (allowed != null && !mapped.Contains(allowed, StringComparer.Ordinal))
                    mapped.Add(allowed);
            }

            AddAllowed(rawGenre.Trim());
            var normalized = NormalizeIdentity(rawGenre);
            if (normalized.Contains("afrohouse")) AddAllowed("Afro House");
            if (normalized.Contains("organichouse")) AddAllowed("Organic House");
            if (normalized.Contains("melodichouse")) AddAllowed("Melodic House");
            if (normalized.Contains("electrohouse") || normalized == "electro") AddAllowed("Electro House");
            if (normalized.Contains("house")) AddAllowed("House");
            if (normalized.Contains("melodictechno")) AddAllowed("Melodic Techno");
            if (normalized.Contains("techno")) AddAllowed("Techno");
            if (normalized.Contains("hardgroove")) AddAllowed("Hardgroove");
            if (normalized.Contains("rap")) AddAllowed("Rap");
            if (normalized.Contains("pop")) AddAllowed("Pop");
            if (normalized.Contains("remix")) AddAllowed("Remix");
            if (normalized.Contains("mashup")) AddAllowed("Mashup");
            if (normalized == "edit" || normalized.EndsWith("edit", StringComparison.Ordinal)) AddAllowed("Edit");

            if (mapped.Count == 0)
                warnings.Add($"Soundcharts genre '{rawGenre}' is not mapped by the workflow taxonomy; the track remains at status Tags");
            else if (!mapped.Any(value => string.Equals(value, rawGenre.Trim(), StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Soundcharts genre '{rawGenre}' mapped to workflow tag(s): {string.Join(", ", mapped)}");

            return mapped;
        }

        private IList<string> MapSituations(IList<string> sourceSituations, IList<string> warnings)
        {
            var mapped = new List<string>();
            if (sourceSituations == null)
                return mapped;

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Peak-time"] = "Peak Time",
                ["Montée"] = "Build-up",
                ["Warm-up"] = "Lounge",
                ["Club"] = "Main Floor",
                ["Festival"] = "Main Floor",
                ["Sunset"] = "Lounge",
                ["Apéro"] = "Lounge",
                ["After"] = "Morning"
            };

            foreach (var source in sourceSituations.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var candidate = aliases.TryGetValue(source, out var alias) ? alias : source;
                var allowed = taxonomy.Situations.FirstOrDefault(value =>
                    string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
                if (allowed == null)
                {
                    warnings.Add($"Airtable situation '{source}' is not represented in the workflow taxonomy and was ignored");
                    continue;
                }

                if (!mapped.Contains(allowed, StringComparer.Ordinal))
                    mapped.Add(allowed);
            }

            return mapped;
        }

        private static string NormalizeContentPath(string path, string databaseDirectory)
        {
            var combined = Path.IsPathRooted(path) ? path : Path.Combine(databaseDirectory, path);
            return Path.GetFullPath(combined)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }
    }

    public sealed class AirtableSyncBatchResult
    {
        public bool Success { get; set; }
        public bool DryRun { get; set; }
        public int SelectedCount { get; set; }
        public IList<AirtableSyncTrackResult> Tracks { get; set; } = new List<AirtableSyncTrackResult>();
        public IList<string> Errors { get; set; } = new List<string>();
    }

    public sealed class AirtableSyncTrackResult
    {
        public string RecordId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Path { get; set; }
        public WorkflowImportResult Import { get; set; }
        public bool AirtableStatusUpdated { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
        public IList<string> Errors { get; set; } = new List<string>();
    }

    internal sealed class AirtableApiClient
    {
        private readonly AirtableSyncOptions options;
        private readonly HttpClient httpClient;

        public AirtableApiClient(AirtableSyncOptions options)
        {
            this.options = options;
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        }

        public IList<AirtableSourceRecord> ListPendingRecords()
        {
            var records = new List<AirtableSourceRecord>();
            string offset = null;
            do
            {
                var query = new List<string>
                {
                    "pageSize=100",
                    "filterByFormula=" + Uri.EscapeDataString(BuildStatusFormula())
                };
                if (!string.IsNullOrWhiteSpace(options.View))
                    query.Add("view=" + Uri.EscapeDataString(options.View));
                if (!string.IsNullOrWhiteSpace(offset))
                    query.Add("offset=" + Uri.EscapeDataString(offset));

                var url = BuildTableUrl() + "?" + string.Join("&", query);
                using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                EnsureSuccess(response, body);
                var page = JObject.Parse(body);
                var pageRecords = page["records"] as JArray ?? new JArray();
                records.AddRange(pageRecords.OfType<JObject>().Select(ParseRecord));
                offset = page.Value<string>("offset");
                if (!string.IsNullOrWhiteSpace(offset))
                    Thread.Sleep(220);
            }
            while (!string.IsNullOrWhiteSpace(offset));

            return records;
        }

        public void UpdateStatuses(IList<string> recordIds, string newStatus)
        {
            const int batchSize = 10;
            for (var index = 0; index < recordIds.Count; index += batchSize)
            {
                var ids = recordIds.Skip(index).Take(batchSize).ToList();
                var recordPayload = new JArray(ids.Select(id => new JObject
                {
                    ["id"] = id,
                    ["fields"] = new JObject { [options.StatusFieldName] = newStatus }
                }));
                var payload = new JObject
                {
                    ["records"] = recordPayload,
                    ["typecast"] = true
                };
                using var request = new HttpRequestMessage(new HttpMethod("PATCH"), BuildTableUrl())
                {
                    Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
                };
                using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                EnsureSuccess(response, body);
                if (index + batchSize < recordIds.Count)
                    Thread.Sleep(220);
            }
        }

        private AirtableSourceRecord ParseRecord(JObject record)
        {
            var fields = record["fields"] as JObject ?? new JObject();
            return new AirtableSourceRecord
            {
                Id = record.Value<string>("id"),
                Title = ReadString(fields, "Titre"),
                Artist = ReadString(fields, "Artiste"),
                SoundchartsGenre = ReadString(fields, "Genre Soundcharts"),
                Energy = ReadInteger(fields, "Énergie"),
                Moods = ReadStringList(fields, "Mood"),
                Situations = ReadStringList(fields, "Situation"),
                SpotifyUrl = ReadString(fields, "Lien Spotify"),
                Comments = ReadString(fields, "Commentaires")
            };
        }

        private string BuildStatusFormula()
        {
            var field = options.StatusFieldName.Replace("}", "\\}");
            var status = options.PendingStatus.Replace("'", "\\'");
            return "{" + field + "}='" + status + "'";
        }

        private string BuildTableUrl()
        {
            return "https://api.airtable.com/v0/" +
                Uri.EscapeDataString(options.BaseId) + "/" +
                Uri.EscapeDataString(options.TableId);
        }

        private static string ReadString(JObject fields, string fieldName)
        {
            var token = fields.GetValue(fieldName, StringComparison.OrdinalIgnoreCase);
            return token?.Type == JTokenType.String ? token.Value<string>()?.Trim() : null;
        }

        private static int? ReadInteger(JObject fields, string fieldName)
        {
            var token = fields.GetValue(fieldName, StringComparison.OrdinalIgnoreCase);
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();
            if (token.Type == JTokenType.Float)
                return Convert.ToInt32(token.Value<double>(), CultureInfo.InvariantCulture);
            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static IList<string> ReadStringList(JObject fields, string fieldName)
        {
            var token = fields.GetValue(fieldName, StringComparison.OrdinalIgnoreCase);
            if (token is JArray array)
                return array.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            if (token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(token.Value<string>()))
                return new List<string> { token.Value<string>().Trim() };
            return new List<string>();
        }

        private static void EnsureSuccess(HttpResponseMessage response, string body)
        {
            if (response.IsSuccessStatusCode)
                return;
            throw new InvalidOperationException(
                $"Airtable API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    internal sealed class AirtableSourceRecord
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string SoundchartsGenre { get; set; }
        public int? Energy { get; set; }
        public IList<string> Moods { get; set; } = new List<string>();
        public IList<string> Situations { get; set; } = new List<string>();
        public string SpotifyUrl { get; set; }
        public string Comments { get; set; }
    }

    internal sealed class RekordboxTrackMatch
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Path { get; set; }
        public string Warning { get; set; }
    }
}
