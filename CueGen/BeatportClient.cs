using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace CueGen
{
    public class BeatportClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private const string ApiBase = "https://api.beatport.com/v4";
        private string _clientId;
        private string _accessToken;
        private DateTime _expiresAt;

        private readonly string _username;
        private readonly string _password;

        public BeatportClient(string username, string password, string clientId = null)
        {
            _username = username;
            _password = password;
            _clientId = clientId;

            var handler = new HttpClientHandler()
            {
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = false
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CueGen/1.0");
        }

        public bool IsConfiguredForAuthentication =>
            !string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_password);

        #region AUTH (SYNC)

        public void Authorize()
        {
            if (!IsConfiguredForAuthentication)
                throw new InvalidOperationException("Beatport credentials are required when Beatport metadata is enabled.");

            if (string.IsNullOrEmpty(_clientId))
                _clientId = FetchClientId();

            _logger.Info("Logging into Beatport...");

            var loginPayload = new
            {
                username = _username,
                password = _password
            };

            var loginResponse = _httpClient.PostAsync(
                $"{ApiBase}/auth/login/",
                new StringContent(JsonConvert.SerializeObject(loginPayload),
                Encoding.UTF8, "application/json")
            ).GetAwaiter().GetResult();

            var loginContent = loginResponse.Content.ReadAsStringAsync()
                .GetAwaiter().GetResult();

            var loginJson = JObject.Parse(loginContent);

            if (loginJson["username"] == null)
                throw new Exception("Beatport login failed.");

            _logger.Info($"Logged in as {loginJson["username"]}");

            var authorizeUrl =
                $"{ApiBase}/auth/o/authorize/?response_type=code&client_id={_clientId}&redirect_uri={ApiBase}/auth/o/post-message/";

            var authResponse = _httpClient.GetAsync(authorizeUrl)
                .GetAwaiter().GetResult();

            if (!authResponse.Headers.Location?.ToString().Contains("code=") ?? true)
                throw new Exception("Authorization code not returned.");

            var codeMatch = Regex.Match(authResponse.Headers.Location.ToString(), @"code=([^&]+)");
            var authCode = codeMatch.Groups[1].Value;

            var tokenUrl =
                $"{ApiBase}/auth/o/token/?code={authCode}&grant_type=authorization_code&redirect_uri={ApiBase}/auth/o/post-message/&client_id={_clientId}";

            var tokenResponse = _httpClient.PostAsync(tokenUrl, null)
                .GetAwaiter().GetResult();

            var tokenContent = tokenResponse.Content.ReadAsStringAsync()
                .GetAwaiter().GetResult();

            var tokenJson = JObject.Parse(tokenContent);

            _accessToken = tokenJson["access_token"]?.ToString();
            var expiresIn = tokenJson["expires_in"]?.ToObject<int>() ?? 3600;

            _expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.Info("Access token received.");
        }

        private string FetchClientId()
        {
            _logger.Info("Fetching Beatport API client ID...");

            var html = _httpClient.GetStringAsync($"{ApiBase}/docs/")
                .GetAwaiter().GetResult();

            var scriptMatches = Regex.Matches(html, @"src=.(.*?\.js)");

            foreach (Match match in scriptMatches)
            {
                var jsUrl = "https://api.beatport.com" + match.Groups[1].Value;
                var js = _httpClient.GetStringAsync(jsUrl)
                    .GetAwaiter().GetResult();

                var idMatch = Regex.Match(js, @"API_CLIENT_ID: '(.+?)'");
                if (idMatch.Success)
                    return idMatch.Groups[1].Value;
            }

            throw new Exception("Could not fetch API_CLIENT_ID.");
        }

        private bool IsTokenExpired()
        {
            return DateTime.UtcNow >= _expiresAt.AddSeconds(-30);
        }

        #endregion

        #region API METHODS (SYNC)

        public TrackResponse GetTracks(
            Dictionary<string, string>? filters = null,
            int? page = null,
            int? perPage = null,
            string? orderBy = null)
        {
            var parameters = new Dictionary<string, string>();

            if (filters != null)
            {
                foreach (var kvp in filters)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                        parameters[kvp.Key] = kvp.Value;
                }
            }

            if (page.HasValue)
                parameters["page"] = page.Value.ToString();

            if (perPage.HasValue)
                parameters["per_page"] = perPage.Value.ToString();

            if (!string.IsNullOrWhiteSpace(orderBy))
                parameters["order_by"] = orderBy;

            return Get<TrackResponse>("/catalog/tracks/", parameters);
        }

        private T Get<T>(string endpoint, Dictionary<string, string> parameters = null)
        {
            if (string.IsNullOrEmpty(_accessToken) || IsTokenExpired())
                Authorize();

            var url = ApiBase + endpoint;

            if (parameters != null && parameters.Count > 0)
            {
                var query = new FormUrlEncodedContent(parameters)
                    .ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();

                url += "?" + query;
            }

            var response = _httpClient.GetAsync(url)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Beatport API error: {response.StatusCode}");

            var content = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();

            return JsonConvert.DeserializeObject<T>(content);
        }
        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public class TrackResponse
        {
            [JsonProperty("results")]
            public List<BeatportTrack> Results { get; set; }

            [JsonProperty("next")]
            public string Next { get; set; }

            [JsonProperty("previous")]
            public string Previous { get; set; }

            [JsonProperty("count")]
            public int Count { get; set; }

            [JsonProperty("page")]
            public string Page { get; set; }

            [JsonProperty("per_page")]
            public int PerPage { get; set; }
        }

        public class BeatportTrack
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Mix_Name { get; set; }
            public int Bpm { get; set; }
            public string Isrc { get; set; }
            public string Length { get; set; }
            public int Length_Ms { get; set; }
            public string Publish_Date { get; set; }
            public bool Is_Explicit { get; set; }
            public bool Is_Available_For_Streaming { get; set; }
            public bool Is_Hype { get; set; }

            public List<BeatportArtist> Artists { get; set; }
            public BeatportGenre Genre { get; set; }
            public BeatportGenre Sub_Genre { get; set; }
            public BeatportKey Key { get; set; }

            public string Sample_Url { get; set; }
        }

        public class BeatportArtist
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Slug { get; set; }
            public string Url { get; set; }
        }

        public class BeatportGenre
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Slug { get; set; }
        }

        public class BeatportKey
        {
            public int Id { get; set; }
            public string Name { get; set; }

            [JsonProperty("camelot_number")]
            public int CamelotNumber { get; set; }

            [JsonProperty("camelot_letter")]
            public string CamelotLetter { get; set; }
        }

    }
}
