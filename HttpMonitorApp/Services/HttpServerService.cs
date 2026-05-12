using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpMonitorApp.Models;

namespace HttpMonitorApp.Services
{
    public class HttpServerService
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<RequestLog> _logs;
        private readonly ConcurrentBag<MessageData> _messages;
        private int _totalRequests;
        private int _getRequests;
        private int _postRequests;
        private double _totalProcessingTime;
        private readonly object _statsLock = new object();
        private DateTime _startTime;

        public event Action<RequestLog> OnRequestLogged;
        public event Action<ServerStats> OnStatsUpdated;
        public bool IsRunning { get; private set; }

        public HttpServerService(ConcurrentQueue<RequestLog> logs)
        {
            _logs = logs;
            _messages = new ConcurrentBag<MessageData>();
        }

        public void Start(int port)
        {
            if (IsRunning) return;

            _startTime = DateTime.Now;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            // Используем localhost вместо + (это не требует прав админа!)
            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
                IsRunning = true;

                // Запускаем обработку в отдельном потоке
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var context = await _listener.GetContextAsync();
                            ThreadPool.QueueUserWorkItem(async __ =>
                            {
                                await HandleRequestAsync(context);
                            });
                        }
                        catch (OperationCanceledException) { break; }
                        catch (HttpListenerException) { break; }
                        catch (Exception ex)
                        {
                            LogToFile($"Ошибка сервера: {ex.Message}");
                        }
                    }
                });

                LogToFile($"Сервер запущен на порту {port}");
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 5) // Access Denied
                {
                    // Пробуем с localhost:port/
                    throw new Exception($"Нет прав на запуск. Попробуйте: netsh http add urlacl url=http://localhost:{port}/ user=Все");
                }
                throw new Exception($"Не удалось запустить сервер: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            IsRunning = false;
            LogToFile("Сервер остановлен");
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var request = context.Request;
            var response = context.Response;

            string requestBody = "";
            if (request.HasEntityBody)
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync();
                }
            }

            var requestLog = new RequestLog
            {
                Timestamp = DateTime.Now,
                Method = request.HttpMethod,
                Url = request.Url?.ToString() ?? "/",
                Headers = FormatHeaders(request.Headers),
                RequestBody = requestBody,
                IsIncoming = true
            };

            try
            {
                string responseText = "";

                switch (request.HttpMethod.ToUpper())
                {
                    case "GET":
                        responseText = GetStatsJson();
                        response.StatusCode = 200;
                        UpdateStats("GET");
                        break;
                    case "POST":
                        (responseText, response.StatusCode) = ProcessPostRequest(requestBody);
                        if (response.StatusCode == 201) UpdateStats("POST");
                        break;
                    default:
                        responseText = "{\"error\":\"Method Not Allowed\"}";
                        response.StatusCode = 405;
                        break;
                }

                stopwatch.Stop();
                requestLog.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                requestLog.StatusCode = response.StatusCode;
                requestLog.ResponseBody = responseText;

                response.ContentType = "application/json; charset=utf-8";
                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);

                _logs.Enqueue(requestLog);
                OnRequestLogged?.Invoke(requestLog);
                OnStatsUpdated?.Invoke(GetStats());
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                requestLog.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                requestLog.StatusCode = 500;
                _logs.Enqueue(requestLog);
                OnRequestLogged?.Invoke(requestLog);
                LogToFile($"Ошибка обработки: {ex.Message}");
            }
            finally
            {
                response.Close();
            }
        }

        private string GetStatsJson()
        {
            var stats = GetStats();
            return JsonSerializer.Serialize(stats, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private (string, int) ProcessPostRequest(string json)
        {
            try
            {
                // Очищаем JSON от лишних символов
                json = json.Trim().Replace("\\\"", "\"");

                var messageData = JsonSerializer.Deserialize<MessageData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (messageData == null || string.IsNullOrEmpty(messageData.Message))
                {
                    return ("{\"error\":\"Invalid JSON. 'message' field is required\"}", 400);
                }

                messageData.Id = Guid.NewGuid();
                _messages.Add(messageData);

                return ($"{{\"id\":\"{messageData.Id}\",\"status\":\"created\"}}", 201);
            }
            catch (JsonException)
            {
                return ("{\"error\":\"Invalid JSON format\"}", 400);
            }
        }

        private void UpdateStats(string method)
        {
            lock (_statsLock)
            {
                _totalRequests++;
                if (method == "GET") _getRequests++;
                else if (method == "POST") _postRequests++;
            }
        }

        public ServerStats GetStats()
        {
            lock (_statsLock)
            {
                return new ServerStats
                {
                    TotalRequests = _totalRequests,
                    GetRequests = _getRequests,
                    PostRequests = _postRequests,
                    AverageProcessingTime = _totalRequests > 0 ? _totalProcessingTime / _totalRequests : 0,
                    ServerStartTime = _startTime
                };
            }
        }

        private string FormatHeaders(System.Collections.Specialized.NameValueCollection headers)
        {
            var sb = new StringBuilder();
            foreach (string key in headers.AllKeys)
            {
                if (key != null)
                    sb.AppendLine($"{key}: {headers[key]}");
            }
            return sb.ToString();
        }

        private void LogToFile(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                File.AppendAllText("server_logs.txt", logMessage);
            }
            catch { }
        }
    }
}
