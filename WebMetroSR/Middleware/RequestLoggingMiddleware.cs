using Serilog.Context;
using System.Collections.Concurrent;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private static readonly ConcurrentDictionary<string, BatchInfo> _batches = new();
        private static readonly TimeSpan _batchIdleTimeout = TimeSpan.FromSeconds(60);

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IUserCredentialService userCreds)
        {
            var path = context.Request.Path.Value ?? "";

            // ---- Explicit batch-end signal ----
            if (path.Equals("/api/ad/batch-end", StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(context.Request.Method))
            {
                var batchId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
                if (!string.IsNullOrEmpty(batchId))
                    FlushBatch(batchId);

                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("OK");
                return;
            }

            // Skip static resources
            if (path.StartsWith("/css/") || path.StartsWith("/js/") ||
                path.EndsWith(".css") || path.EndsWith(".js") ||
                path.EndsWith(".html") || path == "/favicon.ico")
            {
                await _next(context);
                return;
            }

            var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string user;
            try { user = userCreds.GetUsername() ?? "anonymous"; } catch { user = "anonymous"; }
            var requestName = $"{context.Request.Method} {context.Request.Path}";

            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            var hostname = context.Request.Headers["X-Hostname"].FirstOrDefault() ?? "unknown";
            var batchType = context.Request.Headers["X-Batch-Type"].FirstOrDefault() ?? "HostInfo";

            if (!string.IsNullOrEmpty(correlationId))
            {
                await _next(context);

                var statusCode = context.Response.StatusCode;
                var resultMessage = $"HTTP {statusCode} {GetStatusDescription(statusCode)}";
                var requestInfo = $"{requestName} -> {resultMessage}";

                var batch = _batches.GetOrAdd(correlationId, id => new BatchInfo
                {
                    ClientIP = clientIP,
                    User = user,
                    Hostname = hostname,
                    BatchType = batchType,
                    StartTime = DateTime.UtcNow,
                    Requests = new List<string>()
                });

                lock (batch.Requests)
                {
                    batch.Requests.Add(requestInfo);
                }

                ResetBatchTimer(correlationId);
            }
            else
            {
                using (LogContext.PushProperty("ClientIP", clientIP))
                using (LogContext.PushProperty("User", user))
                using (LogContext.PushProperty("RequestName", requestName))
                {
                    try
                    {
                        await _next(context);
                    }
                    finally
                    {
                        var statusCode = context.Response.StatusCode;
                        var resultMessage = $"HTTP {statusCode} {GetStatusDescription(statusCode)}";
                        using (LogContext.PushProperty("ResultMessage", resultMessage))
                        {
                            _logger.LogInformation("Request processed");
                        }
                    }
                }
            }
        }

        private void ResetBatchTimer(string correlationId)
        {
            if (_batches.TryGetValue(correlationId, out var batch))
            {
                lock (batch)
                {
                    batch.FlushTimer?.Dispose();
                    batch.FlushTimer = new Timer(_ => FlushBatch(correlationId), null, _batchIdleTimeout, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void FlushBatch(string correlationId)
        {
            if (_batches.TryRemove(correlationId, out var batch))
            {
                batch.FlushTimer?.Dispose();

                var duration = DateTime.UtcNow - batch.StartTime;
                var requestCount = batch.Requests.Count;
                var summary = $"Batch: {batch.BatchType} for {batch.Hostname}";
                var resultMessage = $"{requestCount} requests, {duration.TotalSeconds:F2}s";

                using (LogContext.PushProperty("ClientIP", batch.ClientIP))
                using (LogContext.PushProperty("User", batch.User))
                using (LogContext.PushProperty("RequestName", summary))
                using (LogContext.PushProperty("ResultMessage", resultMessage))
                {
                    _logger.LogInformation("Batch processed");
                }
            }
        }

        private static string GetStatusDescription(int statusCode) => statusCode switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Unknown"
        };

        private class BatchInfo
        {
            public string ClientIP { get; set; } = string.Empty;
            public string User { get; set; } = string.Empty;
            public string Hostname { get; set; } = string.Empty;
            public string BatchType { get; set; } = "HostInfo";
            public DateTime StartTime { get; set; }
            public List<string> Requests { get; set; } = new();
            public Timer? FlushTimer { get; set; }
        }
    }
}