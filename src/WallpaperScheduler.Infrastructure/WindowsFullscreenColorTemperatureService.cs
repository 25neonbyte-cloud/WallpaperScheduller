using System.Diagnostics;
using System.Runtime.InteropServices;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

/// <summary>
/// Software color-temperature backend for Windows.
///
/// Primary path: Magnification full-screen color matrix. This is a public Win32
/// compositor-level transform and does not depend on downloadable GPU gamma ramps.
/// DDC/CI remains delegated to the existing monitor backend. If the compositor
/// path is unavailable, the legacy gamma-ramp implementation is used as fallback.
/// </summary>
public sealed class WindowsFullscreenColorTemperatureService : IColorTemperatureService
{
    private readonly IAppLogger _logger;
    private readonly WindowsColorTemperatureService _legacy;
    private readonly object _gate = new();

    private bool _magInitialized;
    private bool _capturedOriginalEffect;
    private bool _hasLastEffect;
    private MagColorEffect _originalEffect = MagColorEffect.Identity();
    private MagColorEffect _lastEffect = MagColorEffect.Identity();

    public WindowsFullscreenColorTemperatureService(IAppLogger logger)
    {
        _logger = logger;
        _legacy = new WindowsColorTemperatureService(logger);
    }

    public IReadOnlyList<TemperatureMonitorCapability> GetCapabilities(IReadOnlyList<MonitorResolution> resolutions)
    {
        lock (_gate)
        {
            var legacy = _legacy.GetCapabilities(resolutions);
            var compositorAvailable = EnsureMagnificationInitialized();

            return legacy.Select(x => x with
            {
                SoftwareAvailable = compositorAvailable || x.SoftwareAvailable,
                Message = compositorAvailable
                    ? x.HdrActive
                        ? $"Software via compositor disponível; HDR/Advanced Color detectado. {DescribeDdc(x)}"
                        : $"Software via compositor disponível. {DescribeDdc(x)}"
                    : $"Compositor indisponível; fallback por gamma ramp. {x.Message}"
            }).ToList();
        }
    }

    public ColorTemperatureApplyResult Apply(IReadOnlyList<TemperatureMonitorRequest> requests, bool forceSoftwareConflict)
    {
        lock (_gate)
        {
            if (requests.Count == 0)
                return new(false, [], "Nenhum monitor ativo disponível para temperatura de cor.");

            var ddcRequests = requests
                .Where(x => x.Method == TemperatureApplicationMethod.DdcCi)
                .ToList();
            var softwareRequests = requests
                .Where(x => x.Method != TemperatureApplicationMethod.DdcCi)
                .ToList();

            var statuses = new List<TemperatureMonitorApplyStatus>(requests.Count);
            var messages = new List<string>();

            if (ddcRequests.Count > 0)
            {
                var ddc = _legacy.Apply(ddcRequests, forceSoftwareConflict);
                statuses.AddRange(ddc.Monitors);
                messages.Add(ddc.Message);
            }

            if (softwareRequests.Count > 0)
            {
                var software = ApplySoftwareGlobal(softwareRequests, forceSoftwareConflict);
                statuses.AddRange(software.Monitors);
                messages.Add(software.Message);
            }

            var applied = statuses.Any(x => x.Applied);
            return new(applied, statuses, string.Join(" ", messages.Where(x => !string.IsNullOrWhiteSpace(x))));
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            RestoreCompositorEffect();
            _legacy.Restore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            RestoreCompositorEffect();
            _legacy.Dispose();
            if (_magInitialized)
            {
                _ = MagUninitialize();
                _magInitialized = false;
            }
        }
    }

    private ColorTemperatureApplyResult ApplySoftwareGlobal(
        IReadOnlyList<TemperatureMonitorRequest> requests,
        bool forceSoftwareConflict)
    {
        var requestedKelvins = requests.Select(x => Math.Clamp(x.Kelvin, 3400, 6500)).Distinct().ToList();
        var kelvin = requestedKelvins[0];
        if (requestedKelvins.Count > 1)
        {
            // Software é intencionalmente global. Valores diferentes só podem ser
            // obtidos por hardware DDC/CI em monitores configurados individualmente.
            _logger.Warning("Foram recebidos alvos de temperatura Software diferentes por monitor; a saída global usará o primeiro valor.");
        }

        if (!EnsureMagnificationInitialized())
        {
            var fallback = _legacy.Apply(
                requests.Select(x => x with { Method = TemperatureApplicationMethod.Software }).ToList(),
                forceSoftwareConflict);
            return fallback with { Message = $"Compositor indisponível; fallback gamma. {fallback.Message}" };
        }

        var current = MagColorEffect.Identity();
        if (!MagGetFullscreenColorEffect(ref current))
        {
            var fallback = _legacy.Apply(
                requests.Select(x => x with { Method = TemperatureApplicationMethod.Software }).ToList(),
                forceSoftwareConflict);
            return fallback with { Message = $"Não foi possível ler a transformação do compositor; fallback gamma. {fallback.Message}" };
        }

        if (!_capturedOriginalEffect)
        {
            _originalEffect = current.Clone();
            _capturedOriginalEffect = true;
        }

        var knownFilter = DetectKnownExternalColorFilter();
        var externalCompositorEffect = !EffectClose(current, MagColorEffect.Identity()) &&
                                       (!_hasLastEffect || !EffectClose(current, _lastEffect));

        if ((knownFilter is not null || externalCompositorEffect) && !forceSoftwareConflict)
        {
            var source = knownFilter is not null
                ? $"processo '{knownFilter}'"
                : "outro efeito global de cor do Windows";
            var blocked = requests.Select(x => new TemperatureMonitorApplyStatus(
                x.ProfileId,
                x.ProfileName,
                TemperatureApplicationMethod.Software,
                x.Kelvin,
                null,
                false,
                true,
                false,
                $"Aplicação bloqueada: {source}. Use 'Forçar mesmo com outro filtro detectado' somente se quiser assumir o controle global."))
                .ToList();
            return new(false, blocked, $"Temperatura Software bloqueada por {source}.");
        }

        var target = BuildTemperatureEffect(kelvin);
        if (!MagSetFullscreenColorEffect(ref target))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Warning($"MagSetFullscreenColorEffect falhou (Win32={error}); tentando gamma ramp como fallback.");
            var fallback = _legacy.Apply(
                requests.Select(x => x with { Method = TemperatureApplicationMethod.Software }).ToList(),
                forceSoftwareConflict);
            return fallback with { Message = $"Compositor recusou a transformação (Win32={error}); fallback gamma. {fallback.Message}" };
        }

        var verification = MagColorEffect.Identity();
        var verified = MagGetFullscreenColorEffect(ref verification) && EffectClose(verification, target, 0.01f);
        if (!verified)
            _logger.Warning("O compositor aceitou a transformação, mas a leitura de verificação não correspondeu integralmente ao valor solicitado.");

        _lastEffect = target.Clone();
        _hasLastEffect = true;

        var statuses = requests.Select(x => new TemperatureMonitorApplyStatus(
            x.ProfileId,
            x.ProfileName,
            TemperatureApplicationMethod.Software,
            x.Kelvin,
            kelvin,
            true,
            false,
            false,
            verified
                ? $"{kelvin}K aplicado pelo compositor do Windows."
                : $"{kelvin}K enviado ao compositor do Windows; verificação parcial."))
            .ToList();

        _logger.Info($"Temperatura global Software aplicada pelo compositor: {kelvin}K.");
        return new(true, statuses, $"Temperatura global {kelvin}K aplicada pelo compositor do Windows.");
    }

    private bool EnsureMagnificationInitialized()
    {
        if (_magInitialized) return true;
        try
        {
            _magInitialized = MagInitialize();
            if (_magInitialized)
                _logger.Info("Backend de temperatura Software: Magnification full-screen color effect inicializado.");
            else
                _logger.Warning($"MagInitialize não inicializou o compositor de cor (Win32={Marshal.GetLastWin32Error()}).");
            return _magInitialized;
        }
        catch (DllNotFoundException ex)
        {
            _logger.Error("Magnification.dll não está disponível; temperatura Software usará fallback gamma.", ex);
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger.Error("API de transformação de cor do compositor não está disponível; temperatura Software usará fallback gamma.", ex);
            return false;
        }
    }

    private void RestoreCompositorEffect()
    {
        if (!_capturedOriginalEffect || !_magInitialized) return;
        var restore = _originalEffect.Clone();
        if (!MagSetFullscreenColorEffect(ref restore))
            _logger.Warning($"Falha ao restaurar transformação original do compositor (Win32={Marshal.GetLastWin32Error()}).");
        else
            _logger.Info("Transformação de cor do compositor restaurada ao estado anterior.");

        _capturedOriginalEffect = false;
        _hasLastEffect = false;
        _originalEffect = MagColorEffect.Identity();
        _lastEffect = MagColorEffect.Identity();
    }

    private static string DescribeDdc(TemperatureMonitorCapability capability) =>
        capability.DdcCiAvailable
            ? $"DDC/CI disponível ({string.Join(", ", capability.DdcCiTemperatures.Select(x => $"{x}K"))})."
            : "DDC/CI de temperatura não detectado.";

    private static MagColorEffect BuildTemperatureEffect(int kelvin)
    {
        var reference = TemperatureToRgb(6500);
        var target = TemperatureToRgb(Math.Clamp(kelvin, 3400, 6500));
        var red = (float)Math.Clamp(target.Red / reference.Red, 0.45, 1.0);
        var green = (float)Math.Clamp(target.Green / reference.Green, 0.45, 1.0);
        var blue = (float)Math.Clamp(target.Blue / reference.Blue, 0.35, 1.0);

        return MagColorEffect.Diagonal(red, green, blue);
    }

    private static RgbTemperature TemperatureToRgb(int kelvin)
    {
        var temp = Math.Clamp(kelvin, 1000, 40000) / 100.0;
        double red;
        double green;
        double blue;

        if (temp <= 66)
        {
            red = 255;
            green = 99.4708025861 * Math.Log(temp) - 161.1195681661;
            blue = temp <= 19 ? 0 : 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
        }
        else
        {
            red = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
            green = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
            blue = 255;
        }

        return new(
            Math.Clamp(red, 0, 255),
            Math.Clamp(green, 0, 255),
            Math.Clamp(blue, 0, 255));
    }

    private static bool EffectClose(MagColorEffect left, MagColorEffect right, float tolerance = 0.0025f)
    {
        for (var i = 0; i < 25; i++)
        {
            if (Math.Abs(left.Transform[i] - right.Transform[i]) > tolerance)
                return false;
        }
        return true;
    }

    private static string? DetectKnownExternalColorFilter()
    {
        string[] processNames = ["flux", "LightBulb", "SunsetScreen", "Iris"];
        foreach (var name in processNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                    return name;
            }
            catch { }
        }
        return null;
    }

    private sealed record RgbTemperature(double Red, double Green, double Blue);

    [StructLayout(LayoutKind.Sequential)]
    private struct MagColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] Transform;

        public static MagColorEffect Identity() => Diagonal(1f, 1f, 1f);

        public static MagColorEffect Diagonal(float red, float green, float blue) => new()
        {
            Transform =
            [
                red, 0f, 0f, 0f, 0f,
                0f, green, 0f, 0f, 0f,
                0f, 0f, blue, 0f, 0f,
                0f, 0f, 0f, 1f, 0f,
                0f, 0f, 0f, 0f, 1f
            ]
        };

        public readonly MagColorEffect Clone() => new()
        {
            Transform = (float[])Transform.Clone()
        };
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenColorEffect(ref MagColorEffect effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagGetFullscreenColorEffect(ref MagColorEffect effect);
}
