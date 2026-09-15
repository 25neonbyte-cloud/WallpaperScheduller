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
        VisualRoutineBinding? binding = null;
        if (comfort.Routine.Enabled && evaluation.Winner is { } winner)
            comfort.Routine.Bindings.TryGetValue(winner.Id, out binding);

        var applied = false;
        var messages = new List<string>();

        if (comfort.SystemTheme.Enabled)
        {
            var baseTheme = ResolveBaseTheme(comfort.SystemTheme, localTime);
            var desiredTheme = ResolveTheme(baseTheme, binding);
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
            var baseTargetKelvin = ResolveBaseTemperature(comfort.Temperature, localTime);
            var routineTargetKelvin = binding?.TemperatureKelvin;
            var targetKelvin = Math.Clamp(routineTargetKelvin ?? baseTargetKelvin, 3400, 6500);
            var clockDriven = comfort.Temperature.ControlMode == VisualControlMode.Scheduled && routineTargetKelvin is null;
            var effectiveKelvin = forceReapply || clockDriven
                ? targetKelvin
                : StepTowardTarget(
                    _lastAppliedKelvin,
                    targetKelvin,
                    comfort.Temperature.TransitionMinutes,
                    config.Scheduler.HeartbeatSeconds);

            if (forceReapply || _lastAppliedKelvin != effectiveKelvin)
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
                        effectiveKelvin,
                        method));
                }

                var temperatureResult = colorTemperatureService.Apply(
                    requests,
                    comfort.Temperature.ForceSoftwareWhenExternalTransformDetected);
                applied |= temperatureResult.Applied;
                messages.Add(temperatureResult.Message);

                if (temperatureResult.Applied)
                    _lastAppliedKelvin = effectiveKelvin;

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

    private static int ResolveBaseTemperature(ColorTemperatureSettings settings, TimeOnly localTime)
    {
        if (settings.ControlMode != VisualControlMode.Scheduled || settings.DayStart == settings.NightStart)
            return settings.ManualKelvin;

        var dayKelvin = Math.Clamp(settings.DayKelvin, 3400, 6500);
        var nightKelvin = Math.Clamp(settings.NightKelvin, 3400, 6500);
        var transition = Math.Max(0, settings.TransitionMinutes);
        if (transition == 0)
            return IsWithin(localTime, settings.DayStart, settings.NightStart) ? dayKelvin : nightKelvin;

        var dayStart = ToMinutes(settings.DayStart);
        var nightStart = ToMinutes(settings.NightStart);
        var current = ToMinutes(localTime);
        var daySpan = ForwardMinutes(dayStart, nightStart);
        var nightSpan = ForwardMinutes(nightStart, dayStart);
        var safeTransition = Math.Min(transition, (int)Math.Floor(Math.Min(daySpan, nightSpan)));
        if (safeTransition <= 0)
            return IsWithin(localTime, settings.DayStart, settings.NightStart) ? dayKelvin : nightKelvin;

        var sinceDayStart = ForwardMinutes(dayStart, current);
        if (sinceDayStart < safeTransition)
            return InterpolateKelvin(nightKelvin, dayKelvin, sinceDayStart / safeTransition);

        var sinceNightStart = ForwardMinutes(nightStart, current);
        if (sinceNightStart < safeTransition)
            return InterpolateKelvin(dayKelvin, nightKelvin, sinceNightStart / safeTransition);

        return IsWithin(localTime, settings.DayStart, settings.NightStart) ? dayKelvin : nightKelvin;
    }

    private static bool IsWithin(TimeOnly value, TimeOnly start, TimeOnly end)
    {
        if (start < end)
            return value >= start && value < end;
        return value >= start || value < end;
    }

    private static double ToMinutes(TimeOnly value) =>
        (value.Hour * 60.0) + value.Minute + (value.Second / 60.0) + (value.Millisecond / 60000.0);

    private static double ForwardMinutes(double from, double to)
    {
        var value = to - from;
        return value < 0 ? value + 1440.0 : value;
    }

    private static int InterpolateKelvin(int from, int to, double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        return (int)Math.Round(from + ((to - from) * progress), MidpointRounding.AwayFromZero);
    }

    private static int StepTowardTarget(int? current, int target, int transitionMinutes, int heartbeatSeconds)
    {
        if (current is null || transitionMinutes <= 0) return target;
        var value = current.Value;
        if (value == target) return target;

        var ticks = Math.Max(1.0, transitionMinutes * 60.0 / Math.Max(10, heartbeatSeconds));
        var maxRange = 6500 - 3400;
        var step = Math.Max(25, (int)Math.Ceiling(maxRange / ticks));
        return value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
    }
}
