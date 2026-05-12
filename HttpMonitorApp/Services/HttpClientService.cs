using HttpMonitorApp.Models;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace HttpMonitorApp.Services
{
    public class HttpClientService
    {
        private readonly HttpClient _httpClient;

        public event Action<RequestLog> OnRequestLogged;

        public HttpClientService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<(string response, RequestLog log)> SendGetRequest(string url)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = new RequestLog
            {
                Timestamp = DateTime.Now,
                Method = "GET",
                Url = url,
                IsIncoming = false
            };

            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                stopwatch.Stop();

                log.StatusCode = (int)response.StatusCode;
                log.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                log.ResponseBody = responseBody;
                log.Headers = FormatHeaders(response.Headers);

                OnRequestLogged?.Invoke(log);

                return (responseBody, log);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                log.StatusCode = 0;
                log.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                log.ResponseBody = $"Error: {ex.Message}";

                OnRequestLogged?.Invoke(log);

                return (ex.Message, log);
            }
        }

        public async Task<(string response, RequestLog log)> SendPostRequest(string url, string jsonBody)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = new RequestLog
            {
                Timestamp = DateTime.Now,
                Method = "POST",
                Url = url,
                RequestBody = jsonBody,
                IsIncoming = false
            };

            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                stopwatch.Stop();

                log.StatusCode = (int)response.StatusCode;
                log.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                log.ResponseBody = responseBody;
                log.Headers = FormatHeaders(response.Headers);

                OnRequestLogged?.Invoke(log);

                return (responseBody, log);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                log.StatusCode = 0;
                log.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                log.ResponseBody = $"Error: {ex.Message}";

                OnRequestLogged?.Invoke(log);

                return (ex.Message, log);
            }
        }

        private string FormatHeaders(HttpResponseHeaders headers)
        {
            if (headers == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var header in headers)
            {
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }
            return sb.ToString();
        }
    }
}
