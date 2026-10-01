using Microsoft.AspNetCore.Http.Json;
using Serilog;
using Serilog.Events;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using WebMetroSR.Interfaces;
using WebMetroSR.Middleware;
using WebMetroSR.Services;

namespace WebMetroSR
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Directory.CreateDirectory("logs");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Error) // полное подавление
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ClientIP", "unknown")
                .Enrich.WithProperty("User", "anonymous")
                .Enrich.WithProperty("RequestName", "unknown")
                .Enrich.WithProperty("ResultMessage", "unknown")
                .WriteTo.File(
                    path: Path.Combine("logs", "all.log"),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}, {ClientIP}, {User}, {RequestName}, {ResultMessage}{NewLine}",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31)
                .WriteTo.File(
                    path: Path.Combine("logs", "errors.log"),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}, {ClientIP}, {User}, {RequestName}, {ResultMessage}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    restrictedToMinimumLevel: LogEventLevel.Error)
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(logEvent =>
                        logEvent.Properties.TryGetValue("Category", out var category) &&
                        category.ToString().Trim('"') == "Change")
                    .WriteTo.File(
                        path: Path.Combine("logs", "changes.log"),
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}, {ClientIP}, {User}, {RequestName}, {ResultMessage}{NewLine}",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31))
                .CreateLogger();

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = args,
                    ContentRootPath = AppContext.BaseDirectory,
                    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
                });

                builder.Host.UseSerilog();
                builder.WebHost.UseUrls("http://localhost:5036", "http://localhost:5037");

                // Регистрация сервисов (без изменений)
                builder.Services.AddScoped<IPowerShellService, PowerShellService>();
                builder.Services.AddScoped<IWmiService, WmiService>();
                builder.Services.AddScoped<IMetroScopeService, MetroScopeService>();
                builder.Services.AddScoped<IAdService, AdService>();
                builder.Services.AddScoped<ISccmService, SccmService>();
                builder.Services.AddScoped<IFileService, FileService>();
                builder.Services.AddScoped<IPrintersService, PrintersService>();

                builder.Services.AddMemoryCache();
                builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
                builder.Services.AddHostedService<CleanupBackgroundService>();

                builder.Services.AddDistributedMemoryCache();
                builder.Services.AddSession(options =>
                {
                    options.IdleTimeout = TimeSpan.FromMinutes(30);
                    options.Cookie.HttpOnly = true;
                    options.Cookie.IsEssential = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                });
                builder.Services.AddHttpContextAccessor();
                builder.Services.AddScoped<IUserCredentialService, UserCredentialService>();

                builder.Services.AddControllers();
                builder.Services.AddProblemDetails();

                builder.Services.Configure<JsonOptions>(options =>
                {
                    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
                    options.SerializerOptions.PropertyNameCaseInsensitive = true;
                });

                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowAll", policy =>
                    {
                        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                    });
                });

                var app = builder.Build();

                // Порядок middleware
                app.UseDefaultFiles();
                app.UseStaticFiles();

                app.UseRouting();
                app.UseSession();

                app.UseMiddleware<ExceptionHandlingMiddleware>();
                app.UseMiddleware<RequestLoggingMiddleware>();

                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.UseCors("AllowAll");
                app.UseAuthorization();

                app.MapControllers();

                app.MapGet("/", async context =>
                {
                    context.Response.Redirect("/index.html");
                    await Task.CompletedTask;
                });

                Console.WriteLine("Application started.");
                Console.WriteLine("  HTML: http://localhost:5036/index.html");
                Console.WriteLine("  Login: http://localhost:5036/login.html");

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Критическая ошибка при запуске");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}