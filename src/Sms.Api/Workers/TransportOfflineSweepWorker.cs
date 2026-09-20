using System.Collections.Concurrent;
using Sms.Application.Services.Transport;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Workers;

/// Periodically finds live trips with no recent ping and broadcasts a
/// status_changed(offline) event to that bus's group — the one state
/// transition no ping will ever announce on its own. Mirrors
/// AbsenceAlertWorker's scope-per-sweep + platform-context pattern.
public sealed class TransportOfflineSweepWorker(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<TransportOfflineSweepWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _poll = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("TransportOfflineSweep:PollSeconds") ?? 20, 5, 300));
    private readonly ConcurrentDictionary<Guid, byte> _offline = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transport offline sweep failed");
            }
            await Task.Delay(_poll, stoppingToken);
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(null, null, isPlatform: true);
        var repo = scope.ServiceProvider.GetRequiredService<TripRepository>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<ITransportFleetBroadcaster>();

        var stale = await repo.GetStaleActiveTripsAsync(StaleAfter, ct);
        var staleIds = stale.Select(s => s.TripId).ToHashSet();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            new HashSet<Guid>(_offline.Keys), staleIds);

        foreach (var tripId in toNotify)
        {
            var trip = stale.First(s => s.TripId == tripId);
            _offline[tripId] = 0;
            await broadcaster.BroadcastStatusChangedAsync(trip.BusId, tripId, "offline", ct);
        }
        foreach (var tripId in toClear)
            _offline.TryRemove(tripId, out _);
    }
}
