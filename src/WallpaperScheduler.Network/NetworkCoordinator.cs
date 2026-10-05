using System.Security.Cryptography;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class NetworkCoordinator(
    IConfigStore configStore,
    ControllerServer controller,
    AgentSyncService agent,
    NetworkClockState clockState,
    IAppLogger logger) : INetworkCoordinator
{
    private bool _started;
    private NetworkNodeRole _currentRole = NetworkNodeRole.Standalone;

    public NetworkRuntimeStatus Status =>
        !_started
            ? new(NetworkNodeRole.Standalone, false, false, "Rede não iniciada.")
            : _currentRole switch
            {
                NetworkNodeRole.Controller => new(NetworkNodeRole.Controller, controller.IsRunning, true, controller.IsRunning ? "Controller ativo." : "Controller parado."),
                NetworkNodeRole.Agent => agent.Status,
                _ => new(NetworkNodeRole.Standalone, true, true, "Modo local.")
            };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        var config = await configStore.LoadAsync(cancellationToken);
        var settings = config.Network;
        _currentRole = settings.Role;

        switch (settings.Role)
        {
            case NetworkNodeRole.Controller:
                if (string.IsNullOrWhiteSpace(settings.SharedSecret))
                {
                    settings.SharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    await configStore.SaveAsync(config, cancellationToken);
                    logger.Info("Rede: chave de pareamento do Controller criada automaticamente.");
                }
                await controller.StartAsync(settings, cancellationToken);
                break;

            case NetworkNodeRole.Agent:
                if (string.IsNullOrWhiteSpace(settings.ControllerUrl) || string.IsNullOrWhiteSpace(settings.SharedSecret))
                    logger.Warning("Rede: modo Agent configurado, mas faltam ControllerUrl e/ou chave de pareamento.");
                agent.Start(settings);
                break;

            default:
                clockState.Reset();
                logger.Info("Rede: modo Standalone; nenhuma comunicação LAN foi iniciada.");
                break;
        }

        _started = true;
    }

    public void Dispose()
    {
        agent.Dispose();
        controller.Dispose();
        _started = false;
    }
}
