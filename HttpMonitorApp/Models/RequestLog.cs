using System;

namespace HttpMonitorApp.Models
{
    public class RequestLog
    {
        public DateTime Timestamp { get; set; }
        public string Method { get; set; }
        public string Url { get; set; }
        public string Headers { get; set; }
        public string RequestBody { get; set; }
        public int StatusCode { get; set; }
        public string ResponseBody { get; set; }
        public long ProcessingTimeMs { get; set; }
        public bool IsIncoming { get; set; } // true - входящий, false - исходящий

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {(IsIncoming ? "ВХОД" : "ИСХОД")} {Method} {Url} -> {StatusCode} ({ProcessingTimeMs}ms)";
        }
    }

    public class ServerStats
    {
        public int TotalRequests { get; set; }
        public int GetRequests { get; set; }
        public int PostRequests { get; set; }
        public double AverageProcessingTime { get; set; }
        public DateTime ServerStartTime { get; set; }
        public TimeSpan Uptime => DateTime.Now - ServerStartTime;
    }

    public class MessageData
    {
        public string Message { get; set; }
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
