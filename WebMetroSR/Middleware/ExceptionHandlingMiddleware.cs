using Serilog.Context;
using System.Text.Json;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IUserCredentialService userCreds)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string user;
                try
                {
                    user = userCreds.GetUsername() ?? "anonymous";
                }
                catch
                {
                    user = "anonymous"; // если сессия недоступна
                }
                var requestName = $"{context.Request.Method} {context.Request.Path}";
                var resultMessage = ex.Message;

                using (LogContext.PushProperty("ClientIP", clientIP))
                using (LogContext.PushProperty("User", user))
                using (LogContext.PushProperty("RequestName", requestName))
                using (LogContext.PushProperty("ResultMessage", resultMessage))
                {
                    _logger.LogError(ex, "Unhandled exception");
                }

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    var response = new { error = "Внутренняя ошибка сервера", detail = ex.Message };
                    var json = JsonSerializer.Serialize(response);
                    await context.Response.WriteAsync(json);
                }
                else
                {
                    await context.Response.CompleteAsync();
                }
            }
        }
    }
}