using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Workers;

/// <summary>
/// Background worker xử lý hết hạn đơn đặt chỗ quá hạn 15 phút (BR-RSV-01) chạy trong Web Host.
/// Tự động chuyển các đơn 'pending'/'awaiting_deposit' có hold_until nhỏ hơn thời gian hiện tại sang 'expired'.
/// Vận hành trên vòng đời Scoped riêng biệt ở mỗi chu kỳ lặp, tuyệt đối không inject trực tiếp DbContext vào worker.
/// Khả năng chống chịu khởi động lại (Restart Resilience): chu kỳ đầu tiên chạy ngay lập tức sau khi startup
/// để dọn dẹp tất cả các đơn giữ chỗ đã hết hạn trong lúc máy chủ tắt.
/// </summary>
public class ReservationHoldExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationHoldExpirationWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IReservationExpirationHook? _injectedHook;
    private readonly TimeSpan _checkInterval;

    public ReservationHoldExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationHoldExpirationWorker> logger,
        IConfiguration configuration,
        IReservationExpirationHook? injectedHook = null,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _injectedHook = injectedHook;

        if (checkInterval.HasValue)
        {
            _checkInterval = checkInterval.Value;
        }
        else
        {
            var intervalSeconds = _configuration.GetValue<int?>("ReservationWorker:IntervalSeconds") ?? 30;
            _checkInterval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = _configuration.GetValue<bool?>("ReservationWorker:Enabled") ?? true;
        if (!isEnabled)
        {
            _logger.LogInformation("ReservationHoldExpirationWorker is disabled via configuration.");
            return;
        }

        _logger.LogInformation(
            "Reservation hold expiration worker started in Web host. Cycle interval: {IntervalSeconds}s.",
            _checkInterval.TotalSeconds);

        // Chu kỳ đầu tiên chạy ngay sau startup để giải phóng các hold đã hết hạn khi máy chủ offline
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();

                var expiredHolds = await reservationService.ExpireOverdueHoldsDetailedAsync(stoppingToken);
                if (expiredHolds.Count > 0)
                {
                    _logger.LogInformation(
                        "Web Worker successfully expired {Count} overdue reservation hold(s) and released capacity.",
                        expiredHolds.Count);

                    var hook = _injectedHook ?? scope.ServiceProvider.GetService<IReservationExpirationHook>();
                    if (hook != null)
                    {
                        try
                        {
                            await hook.OnHoldsExpiredWithDetailsAsync(expiredHolds, stoppingToken);
                        }
                        catch (Exception hookEx)
                        {
                            _logger.LogWarning(hookEx, "Error executing post-expiration hook in Web worker.");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Web host reservation hold expiration worker cycle.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Reservation hold expiration worker stopped in Web host.");
    }
}
