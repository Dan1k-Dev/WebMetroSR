using WebMetroSR.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection; 
namespace WebMetroSR.Services
{
    public class CleanupBackgroundService : BackgroundService
    {
        private readonly ILogger<CleanupBackgroundService> _logger;
        private readonly IServiceScopeFactory _scopeFactory; 
        private readonly ICacheService _cache;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1);

        // Конструктор теперь принимает IServiceScopeFactory вместо IMetroScopeService
        public CleanupBackgroundService(ILogger<CleanupBackgroundService> logger, IServiceScopeFactory scopeFactory, ICacheService cache)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _cache = cache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Создаем временную область видимости для scoped-сервиса
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Получаем сервис из созданного scope
                        var metroScopeService = scope.ServiceProvider.GetRequiredService<IMetroScopeService>();

                        // Вызываем ваш метод очистки
                        metroScopeService.CleanOldTempFiles();
                    }

                    // Очистка устаревшего кэша не требуется – MemoryCache сам вытесняет по expiration
                    _logger.LogInformation("Cleanup completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cleanup failed");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
