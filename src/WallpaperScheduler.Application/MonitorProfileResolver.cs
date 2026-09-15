using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed class MonitorProfileResolver(
    IMonitorBindingStore bindingStore,
    IConfigStore configStore) : IMonitorProfileResolver
{
    public async Task<IReadOnlyList<MonitorResolution>> ResolveAsync(
        AppConfig config,
        IReadOnlyList<MonitorInfo> monitors,
        CancellationToken cancellationToken = default)
    {
        var bindings = (await bindingStore.LoadAsync(cancellationToken)).ToList();
        var resolutions = new List<MonitorResolution>();
        var usedMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changedBindings = false;
        var changedConfig = false;

        foreach (var profile in config.MonitorProfiles)
        {
            var binding = bindings.FirstOrDefault(x => x.ProfileId == profile.Id);
            var match = ResolveExisting(binding, monitors, usedMonitorIds, out var ambiguous, out var status);

            if (match is not null)
            {
                usedMonitorIds.Add(match.Id);
                var updated = new MonitorBinding(profile.Id, match.HardwareKey, match.Id, match.Width, match.Height);
                ReplaceBinding(bindings, updated);
                changedBindings = true;
            }

            resolutions.Add(new(profile.Id, profile.Name, match, ambiguous, status));
        }

        var unmatched = monitors.Where(x => !usedMonitorIds.Contains(x.Id)).ToList();
        foreach (var monitor in unmatched)
        {
            var profile = new MonitorProfile
            {
                Name = NextProfileName(config.MonitorProfiles)
            };
            config.MonitorProfiles.Add(profile);
            changedConfig = true;

            var binding = new MonitorBinding(profile.Id, monitor.HardwareKey, monitor.Id, monitor.Width, monitor.Height);
            bindings.Add(binding);
            changedBindings = true;
            usedMonitorIds.Add(monitor.Id);
            resolutions.Add(new(profile.Id, profile.Name, monitor, false, "Associado automaticamente neste computador."));
        }

        if (changedBindings)
            await bindingStore.SaveAsync(bindings, cancellationToken);
        if (changedConfig)
            await configStore.SaveAsync(config, cancellationToken);

        return resolutions;
    }

    private static MonitorInfo? ResolveExisting(
        MonitorBinding? binding,
        IReadOnlyList<MonitorInfo> monitors,
        HashSet<string> usedMonitorIds,
        out bool ambiguous,
        out string status)
    {
        ambiguous = false;
        status = "Monitor não disponível.";
        if (binding is null) return null;

        if (!string.IsNullOrWhiteSpace(binding.LastKnownDevicePath))
        {
            var exact = monitors.FirstOrDefault(x =>
                !usedMonitorIds.Contains(x.Id) &&
                string.Equals(x.Id, binding.LastKnownDevicePath, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                status = "Reconhecido pelo vínculo local persistente.";
                return exact;
            }
        }

        if (!string.IsNullOrWhiteSpace(binding.HardwareKey))
        {
            var hardwareMatches = monitors.Where(x =>
                    !usedMonitorIds.Contains(x.Id) &&
                    string.Equals(x.HardwareKey, binding.HardwareKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (hardwareMatches.Count == 1)
            {
                status = "Reconhecido pela identidade de hardware.";
                return hardwareMatches[0];
            }

            if (hardwareMatches.Count > 1)
            {
                ambiguous = true;
                status = "Associação ambígua: há monitores fisicamente indistinguíveis. Nenhuma troca automática foi feita.";
                return null;
            }
        }

        var sizeMatches = monitors.Where(x =>
                !usedMonitorIds.Contains(x.Id) &&
                binding.LastKnownWidth == x.Width &&
                binding.LastKnownHeight == x.Height)
            .ToList();

        if (sizeMatches.Count == 1)
        {
            status = "Reconhecido conservadoramente por resolução única.";
            return sizeMatches[0];
        }

        if (sizeMatches.Count > 1)
        {
            ambiguous = true;
            status = "Associação ambígua: múltiplos monitores têm as mesmas características conhecidas.";
        }

        return null;
    }

    private static void ReplaceBinding(List<MonitorBinding> bindings, MonitorBinding updated)
    {
        var index = bindings.FindIndex(x => x.ProfileId == updated.ProfileId);
        if (index >= 0) bindings[index] = updated;
        else bindings.Add(updated);
    }

    private static string NextProfileName(IReadOnlyCollection<MonitorProfile> profiles)
    {
        var index = profiles.Count + 1;
        string name;
        do { name = $"Monitor {index++}"; }
        while (profiles.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }
}
