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
        var bindingsByProfile = bindings
            .GroupBy(x => x.ProfileId)
            .ToDictionary(x => x.Key, x => x.Last());

        var resolved = new Dictionary<Guid, MonitorInfo>();
        var statuses = new Dictionary<Guid, string>();
        var ambiguousProfiles = new HashSet<Guid>();
        var usedMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changedBindings = false;
        var changedConfig = false;

        // 1) Device path conhecido tem precedência global. Isso impede um perfil antigo
        // de capturar por fallback de hardware um monitor que ainda pertence a outro
        // perfil por vínculo exato.
        foreach (var profile in config.MonitorProfiles)
        {
            if (!bindingsByProfile.TryGetValue(profile.Id, out var binding) ||
                string.IsNullOrWhiteSpace(binding.LastKnownDevicePath))
                continue;

            var exact = monitors.FirstOrDefault(x =>
                !usedMonitorIds.Contains(x.Id) &&
                string.Equals(x.Id, binding.LastKnownDevicePath, StringComparison.OrdinalIgnoreCase));

            if (exact is null) continue;
            Resolve(profile.Id, exact, "Reconhecido pelo vínculo local persistente.");
        }

        // 2) Se o device path mudou, tenta identidade de hardware. Só aceita quando
        // existe exatamente um candidato livre; monitores idênticos permanecem ambíguos.
        foreach (var profile in config.MonitorProfiles)
        {
            if (resolved.ContainsKey(profile.Id)) continue;
            if (!bindingsByProfile.TryGetValue(profile.Id, out var binding))
            {
                statuses[profile.Id] = "Sem vínculo local.";
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.HardwareKey)) continue;

            var matches = monitors.Where(x =>
                    !usedMonitorIds.Contains(x.Id) &&
                    string.Equals(x.HardwareKey, binding.HardwareKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                Resolve(profile.Id, matches[0], "Reconhecido pela identidade de hardware.");
            }
            else if (matches.Count > 1)
            {
                ambiguousProfiles.Add(profile.Id);
                statuses[profile.Id] = "Associação ambígua: há monitores fisicamente indistinguíveis. Nenhuma troca automática foi feita.";
            }
        }

        // 3) Último fallback: resolução, somente se ela também for única.
        foreach (var profile in config.MonitorProfiles)
        {
            if (resolved.ContainsKey(profile.Id) || ambiguousProfiles.Contains(profile.Id)) continue;
            if (!bindingsByProfile.TryGetValue(profile.Id, out var binding)) continue;

            var matches = monitors.Where(x =>
                    !usedMonitorIds.Contains(x.Id) &&
                    binding.LastKnownWidth == x.Width &&
                    binding.LastKnownHeight == x.Height)
                .ToList();

            if (matches.Count == 1)
            {
                Resolve(profile.Id, matches[0], "Reconhecido conservadoramente por resolução única.");
            }
            else if (matches.Count > 1)
            {
                ambiguousProfiles.Add(profile.Id);
                statuses[profile.Id] = "Associação ambígua: múltiplos monitores têm as mesmas características conhecidas.";
            }
            else if (!statuses.ContainsKey(profile.Id))
            {
                statuses[profile.Id] = "Monitor não disponível.";
            }
        }

        var resolutions = new List<MonitorResolution>();
        foreach (var profile in config.MonitorProfiles)
        {
            resolved.TryGetValue(profile.Id, out var monitor);
            resolutions.Add(new(
                profile.Id,
                profile.Name,
                monitor,
                ambiguousProfiles.Contains(profile.Id),
                statuses.GetValueOrDefault(profile.Id, "Monitor não disponível.")));
        }

        // Em presença de ambiguidade, não cria perfis automaticamente. Isso evita
        // duplicar perfis ou associar um monitor físico ao perfil errado.
        if (ambiguousProfiles.Count == 0)
        {
            foreach (var monitor in monitors.Where(x => !usedMonitorIds.Contains(x.Id)))
            {
                var profile = new MonitorProfile { Name = NextProfileName(config.MonitorProfiles) };
                config.MonitorProfiles.Add(profile);
                changedConfig = true;

                var binding = new MonitorBinding(profile.Id, monitor.HardwareKey, monitor.Id, monitor.Width, monitor.Height);
                bindings.Add(binding);
                bindingsByProfile[profile.Id] = binding;
                changedBindings = true;
                usedMonitorIds.Add(monitor.Id);
                resolutions.Add(new(profile.Id, profile.Name, monitor, false, "Associado automaticamente neste computador."));
            }
        }

        if (changedBindings)
            await bindingStore.SaveAsync(bindings, cancellationToken);
        if (changedConfig)
            await configStore.SaveAsync(config, cancellationToken);

        return resolutions;

        void Resolve(Guid profileId, MonitorInfo monitor, string status)
        {
            resolved[profileId] = monitor;
            statuses[profileId] = status;
            usedMonitorIds.Add(monitor.Id);

            var updated = new MonitorBinding(profileId, monitor.HardwareKey, monitor.Id, monitor.Width, monitor.Height);
            if (!bindingsByProfile.TryGetValue(profileId, out var current) || current != updated)
            {
                ReplaceBinding(bindings, updated);
                bindingsByProfile[profileId] = updated;
                changedBindings = true;
            }
        }
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
