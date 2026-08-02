using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace CueGen
{
    public class SoundchartsClient : IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly HttpClient _client;
        private readonly string _appId;
        private readonly string _apiKey;

        public SoundchartsClient(string appId, string apiKey)
        {
            _appId = appId;
            _apiKey = apiKey;
            _client = new HttpClient();
            _client.BaseAddress = new Uri("https://customer.api.soundcharts.com/api/v2.25/");
            _client.DefaultRequestHeaders.Add("x-app-id", _appId);
            _client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        }

        public async Task<SoundchartsSong> GetSongByUuidAsync(string uuid)
        {
            Log.Info("Fetching song metadata from Soundcharts for UUID {uuid}", uuid);
            var response = await _client.GetAsync($"song/{uuid}");
            return await HandleResponse<SoundchartsSong>(response);
        }

        public async Task<SoundchartsSong> GetSongByIsrcAsync(string isrc)
        {
            Log.Info("Fetching song metadata from Soundcharts for ISRC {isrc}", isrc);
            var response = await _client.GetAsync($"song/by-isrc/{isrc}");
            return await HandleResponse<SoundchartsSong>(response);
        }

        private async Task<T> HandleResponse<T>(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("Soundcharts API error: {statusCode} - {content}", response.StatusCode, errorContent);
                return default;
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<SoundchartsResponse<T>>(content);

            if (result?.Errors != null && result.Errors.Count > 0)
            {
                Log.Error("Soundcharts API returned errors: {errors}", string.Join(", ", result.Errors));
                return default;
            }

            return result.Object;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
