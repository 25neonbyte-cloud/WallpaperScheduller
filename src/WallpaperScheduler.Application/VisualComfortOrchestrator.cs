using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Application;

public sealed class VisualComfortOrchestrator(
    IConfigStore configStore,
    IRuleEngine ruleEngine,
    IMonitorService monitorService,
    IMonitorProfileResolver monitorProfileResolver,
    ISystemThemeService systemThemeService,
    IColorTemperatureService colorTemperatureService,
    IClock clock,
    IAppLogger logger)
{
    private int? _lastAppliedKelvin;
    private SystemThemeMode? _lastTheme;
    private bool _wasActive;

    public Task<VisualComfortApplyResult> ApplyCurrentAsync(CancellationToken cancellationToken = default) =>
        ApplyCurrentCoreAsync(forceReapply: false, cancellationToken);

    public Task<VisualComfortApplyResult> ReapplyCurrentAsync(CancellationToken cancellationToken = default) =>
        ApplyCurrentCoreAsync(forceReapply: true, cancellationToken);

    public async Task<IReadOnlyList<TemperatureMonitorCapability>> GetTemperatureCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var monitors = monitorService.GetActiveMonitors();
        var resolutions = await monitorProfileResolver.ResolveAsync(config, monitors, cancellationToken);
        return colorTemperatureService.GetCapabilities(resolutions);
    }

    public void Restore()
    {
        systemThemeService.Restore();
        colorTemperatureService.Restore();
        _lastAppliedKelvin = null;
        _lastTheme = null;
        _wasActive = false;
    }

    private async Task<VisualComfortApplyResult> ApplyCurrentCoreAsync(bool forceReapply, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var comfort = config.VisualComfort;
        if (!comfort.Enabled)
        {
            if (_wasActive) Restore();
            return new(false, "Conforto visual desligado.");
        }

        _wasActive = true;
        var now = clock.Now;
        var localTime = TimeOnly.FromDateTime(now.LocalDateTime);
        var evaluation = ruleEngine.Evaluate(config.Rules, now);
        VisualRoutineBinding? currentBinding = null;
        if (comfort.Routine.Enabled && evaluation.Winner is { } winner)
            comfort.Routine.Bindings.TryGetValue(winner.Id, out currentBinding);

        var applied = false;
        var messages = new List<string>();

        if (comfort.SystemTheme.Enabled)
        {
            var baseTheme = ResolveBaseTheme(comfort.SystemTheme, localTime);
            var desiredTheme = ResolveTheme(baseTheme, currentBinding);
            if (forceReapply || _lastTheme != desiredTheme)
            {
                var themeResult = systemThemeService.Apply(desiredTheme);
                applied |= themeResult.Applied;
                _lastTheme = desiredTheme;
                messages.Add(themeResult.Message);
            }
        }
        else if (_lastTheme is not null)
        {
            systemThemeService.Restore();
            _lastTheme = null;
            messages.Add("Tema restaurado ao estado anterior.");
        }

        if (comfort.Temperature.Enabled)
        {
            var targetKelvin = ResolveTemperatureTarget(config, comfort, localTime);
            logger.Info($"Temperatura alvo pela curva: hora={localTime.ToString("HH:mm")}; dia={Math.Clamp(comfort.Temperature.DayKelvin, 3400, 6500)}K; noite={Math.Clamp(comfort.Temperature.NightKelvin, 3400, 6500)}K; alvo={targetKelvin}K.");

            // A curva é função do relógio, não do instante em que o processo iniciou.
            // Cada heartbeat apenas amostra novamente a posição atual da onda.
            if (forceReapply || _lastAppliedKelvin != targetKelvin)
            {
                var monitors = monitorService.GetActiveMonitors();
                var resolutions = await monitorProfileResolver.ResolveAsync(config, monitors, cancellationToken);
                var requests = new List<TemperatureMonitorRequest>();

                foreach (var resolution in resolutions.Where(x => x.Monitor is not null && !x.IsAmbiguous))
                {
                    var method = comfort.Temperature.PerMonitorMethods.TryGetValue(resolution.ProfileId, out var overrideMethod)
                        ? overrideMethod
                        : comfort.Temperature.Method;
                    requests.Add(new(
                        resolution.ProfileId,
                        resolution.ProfileName,
                        resolution.Monitor!,
                        targetKelvin,
                        method));
                }

                var temperatureResult = colorTemperatureService.Apply(
                    requests,
                    comfort.Temperature.ForceSoftwareWhenExternalTransformDetected);
                applied |= temperatureResult.Applied;
                messages.Add(temperatureResult.Message);

                if (temperatureResult.Applied)
                    _lastAppliedKelvin = targetKelvin;

                foreach (var status in temperatureResult.Monitors.Where(x => !x.Applied))
                    logger.Warning($"Temperatura [{status.ProfileName}]: {status.Message}");
            }
        }
        else if (_lastAppliedKelvin is not null)
        {
            colorTemperatureService.Restore();
            _lastAppliedKelvin = null;
            messages.Add("Temperatura restaurada ao estado anterior.");
        }

        if (messages.Count == 0)
            messages.Add(evaluation.HasMatch && comfort.Routine.Enabled
                ? $"Rotina visual sincronizada com '{evaluation.Winner!.Name}'."
                : "Estado de conforto visual já aplicado.");

        return new(applied, string.Join(" ", messages));
    }

    private static SystemThemeMode ResolveBaseTheme(SystemThemeSettings settings, TimeOnly localTime)
    {
        if (settings.ControlMode != VisualControlMode.Scheduled || settings.LightStart == settings.DarkStart)
            return settings.ManualMode;

        return IsWithin(localTime, settings.LightStart, settings.DarkStart)
            ? SystemThemeMode.Light
            : SystemThemeMode.Dark;
    }

    private static SystemThemeMode ResolveTheme(SystemThemeMode baseTheme, VisualRoutineBinding? binding) =>
        binding?.Theme switch
        {
            VisualRoutineThemeTarget.Light => SystemThemeMode.Light,
            VisualRoutineThemeTarget.Dark => SystemThemeMode.Dark,
            _ => baseTheme
        };

    private static int ResolveTemperatureTarget(AppConfig config, VisualComfortSettings comfort, TimeOnly localTime)
    {
        var settings = comfort.Temperature;

        // A UX atual da temperatura é exclusivamente uma curva contínua de 24 h.
        // Builds anteriores persistiam ControlMode=Manual e, mesmo após a remoção
        // desse seletor da interface, isso fazia o orquestrador retornar ManualKelvin
        // (6500K por padrão) e ignorar completamente o perfil noturno configurado.
        // Enquanto não existir novamente um override manual explícito na UX, a fonte
        // de verdade é sempre a curva calculada pelo relógio local.

        var enabledRules = config.Rules.Where(x => x.Enabled).ToList();
        var explicitBindings = comfort.Routine.Enabled
            ? enabledRules
                .Where(x => comfort.Routine.Bindings.TryGetValue(x.Id, out var binding) && binding.TemperatureKelvin is not null)
                .Select(x => new
                {
                    Rule = x,
                    Kelvin = Math.Clamp(comfort.Routine.Bindings[x.Id].TemperatureKelvin!.Value, 3400, 6500)
                })
                .ToList()
            : [];

        // Um único período cobrindo toda a rotina continua podendo funcionar como
        // temperatura fixa explícita. Com vários períodos, os valores explícitos
        // passam a ser pontos da curva e não degraus instantâneos.
        if (enabledRules.Count == 1 && explicitBindings.Count == 1)
            return explicitBindings[0].Kelvin;

        var dayKelvin = Math.Clamp(settings.DayKelvin, 3400, 6500);
        var nightKelvin = Math.Clamp(settings.NightKelvin, 3400, 6500);
        var phases = ResolvePhaseStarts(enabledRules);

        var anchors = new SortedDictionary<int, int>
        {
            [Minutes(phases.Madrugada)] = nightKelvin,
            [Minutes(phases.Manha)] = nightKelvin,
            [Minutes(phases.Dia)] = dayKelvin,
            [Minutes(phases.Tarde)] = dayKelvin,
            [Minutes(phases.Noite)] = nightKelvin
        };

        foreach (var point in explicitBindings)
            anchors[Minutes(point.Rule.Start)] = point.Kelvin;

        return EvaluateCircularCurve(anchors, localTime);
    }

    private static PhaseStarts ResolvePhaseStarts(IReadOnlyCollection<WallpaperRule> rules)
    {
        static bool Has(string source, params string[] values) =>
            values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));

        TimeOnly? Find(params string[] values) => rules
            .Where(x => Has(x.Name, values))
            .OrderBy(x => x.Start)
            .Select(x => (TimeOnly?)x.Start)
            .FirstOrDefault();

        var madrugada = Find("madrugada", "meia-noite");
        var manha = Find("manhã", "manha", "nascer");
        var dia = rules
            .Where(x => Has(x.Name, "dia") && !Has(x.Name, "madrugada"))
            .OrderBy(x => x.Start)
            .Select(x => (TimeOnly?)x.Start)
            .FirstOrDefault();
        var tarde = Find("tarde", "entardecer", "pôr", "por do sol");
        var noite = Find("noite");

        if (madrugada is not null && manha is not null && dia is not null && tarde is not null && noite is not null)
            return new(madrugada.Value, manha.Value, dia.Value, tarde.Value, noite.Value);

        var starts = rules
            .Select(x => x.Start)
            .Distinct()
            .OrderBy(x => x)
            .Take(5)
            .ToList();

        if (starts.Count == 5)
            return new(starts[0], starts[1], starts[2], starts[3], starts[4]);

        // Fallback de UX somente quando não existem cinco períodos utilizáveis.
        return new(
            new TimeOnly(0, 0),
            new TimeOnly(6, 0),
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            new TimeOnly(20, 0));
    }

    private static int EvaluateCircularCurve(SortedDictionary<int, int> anchors, TimeOnly localTime)
    {
        var points = anchors.ToArray();
        if (points.Length == 0) return 6500;
        if (points.Length == 1) return points[0].Value;

        var current = Minutes(localTime);
        for (var i = 0; i < points.Length - 1; i++)
        {
            var left = points[i];
            var right = points[i + 1];
            if (current >= left.Key && current < right.Key)
                return SmoothInterpolate(left.Value, right.Value, current - left.Key, right.Key - left.Key);
        }

        var last = points[^1];
        var first = points[0];
        var wrappedCurrent = current < first.Key ? current + 1440 : current;
        var wrappedFirst = first.Key + 1440;
        return SmoothInterpolate(last.Value, first.Value, wrappedCurrent - last.Key, wrappedFirst - last.Key);
    }

    private static int SmoothInterpolate(int from, int to, double elapsed, double duration)
    {
        if (duration <= 0 || from == to) return to;
        var progress = Math.Clamp(elapsed / duration, 0.0, 1.0);
        var smooth = progress * progress * (3.0 - (2.0 * progress));
        return (int)Math.Round(from + ((to - from) * smooth), MidpointRounding.AwayFromZero);
    }

    private static bool IsWithin(TimeOnly value, TimeOnly start, TimeOnly end)
    {
        if (start < end)
            return value >= start && value < end;
        return value >= start || value < end;
    }

    private static int Minutes(TimeOnly value) => (value.Hour * 60) + value.Minute;

    private sealed record PhaseStarts(TimeOnly Madrugada, TimeOnly Manha, TimeOnly Dia, TimeOnly Tarde, TimeOnly Noite);
}
