using System.Diagnostics;
using System.Runtime.InteropServices;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class WindowsColorTemperatureService(IAppLogger logger) : IColorTemperatureService
{
    private const uint McCapsColorTemperature = 0x00000008;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;

    private readonly object _gate = new();
    private readonly Dictionary<string, GammaRamp> _originalGamma = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GammaRamp> _lastGamma = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McColorTemperature> _originalDdcTemperature = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TemperatureMonitorCapability> GetCapabilities(IReadOnlyList<MonitorResolution> resolutions)
    {
        lock (_gate)
        {
            var targets = EnumerateDisplayTargets();
            var hdrByDevice = GetAdvancedColorStateByDevice();
            var result = new List<TemperatureMonitorCapability>();

            foreach (var resolution in resolutions.Where(x => x.Monitor is not null && !x.IsAmbiguous))
            {
                var monitor = resolution.Monitor!;
                var target = FindTarget(monitor, targets);
                if (target is null)
                {
                    result.Add(new(
                        resolution.ProfileId,
                        resolution.ProfileName,
                        false,
                        false,
                        false,
                        [],
                        "Não foi possível associar o monitor lógico à saída física do Windows."));
                    continue;
                }

                var hdrActive = hdrByDevice.TryGetValue(target.DeviceName, out var hdr) && hdr;
                var ddcTemperatures = GetDdcSupportedTemperatures(target);
                var message = hdrActive
                    ? "HDR/Advanced Color ativo: o motor por software fica bloqueado; DDC/CI continua disponível quando suportado."
                    : ddcTemperatures.Count > 0
                        ? $"Software disponível; DDC/CI detectado ({string.Join(", ", ddcTemperatures.Select(x => $"{x}K"))})."
                        : "Software disponível; DDC/CI de temperatura não detectado.";

                result.Add(new(
                    resolution.ProfileId,
                    resolution.ProfileName,
                    true,
                    hdrActive,
                    ddcTemperatures.Count > 0,
                    ddcTemperatures,
                    message));
            }

            return result;
        }
    }

    public ColorTemperatureApplyResult Apply(IReadOnlyList<TemperatureMonitorRequest> requests, bool forceSoftwareConflict)
    {
        lock (_gate)
        {
            if (requests.Count == 0)
                return new(false, [], "Nenhum monitor ativo disponível para temperatura de cor.");

            var targets = EnumerateDisplayTargets();
            var hdrByDevice = GetAdvancedColorStateByDevice();
            var statuses = new List<TemperatureMonitorApplyStatus>(requests.Count);

            foreach (var request in requests)
            {
                var target = FindTarget(request.Monitor, targets);
                if (target is null)
                {
                    statuses.Add(new(
                        request.ProfileId,
                        request.ProfileName,
                        request.Method,
                        request.Kelvin,
                        null,
                        false,
                        false,
                        false,
                        "Saída física do monitor não encontrada."));
                    continue;
                }

                var method = request.Method == TemperatureApplicationMethod.Automatic
                    ? TemperatureApplicationMethod.Software
                    : request.Method;

                if (method == TemperatureApplicationMethod.DdcCi)
                {
                    statuses.Add(ApplyDdc(request, target));
                    continue;
                }

                var hdrActive = hdrByDevice.TryGetValue(target.DeviceName, out var hdr) && hdr;
                if (hdrActive)
                {
                    statuses.Add(new(
                        request.ProfileId,
                        request.ProfileName,
                        TemperatureApplicationMethod.Software,
                        request.Kelvin,
                        null,
                        false,
                        false,
                        true,
                        "Software bloqueado porque HDR/Advanced Color está ativo nesta saída."));
                    continue;
                }

                statuses.Add(ApplySoftware(request, target, forceSoftwareConflict));
            }

            var appliedCount = statuses.Count(x => x.Applied);
            var conflictCount = statuses.Count(x => x.ConflictDetected);
            var hdrCount = statuses.Count(x => x.HdrBlocked);
            var message = $"Temperatura aplicada em {appliedCount}/{statuses.Count} monitor(es).";
            if (conflictCount > 0) message += $" {conflictCount} conflito(s) externo(s) detectado(s).";
            if (hdrCount > 0) message += $" {hdrCount} monitor(es) bloqueado(s) por HDR/Advanced Color.";

            return new(appliedCount > 0, statuses, message);
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            RestoreGammaRamps();
            RestoreDdcTemperatures();
        }
    }

    public void Dispose() => Restore();

    private TemperatureMonitorApplyStatus ApplySoftware(
        TemperatureMonitorRequest request,
        DisplayTarget target,
        bool forceSoftwareConflict)
    {
        var hdc = CreateDC("DISPLAY", target.DeviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return new(
                request.ProfileId,
                request.ProfileName,
                TemperatureApplicationMethod.Software,
                request.Kelvin,
                null,
                false,
                false,
                false,
                "O Windows não forneceu um contexto de vídeo para esta saída.");
        }

        try
        {
            var current = GammaRamp.Create();
            if (!GetDeviceGammaRamp(hdc, ref current))
            {
                return new(
                    request.ProfileId,
                    request.ProfileName,
                    TemperatureApplicationMethod.Software,
                    request.Kelvin,
                    null,
                    false,
                    false,
                    false,
                    "O driver não expõe uma gamma/color ramp utilizável.");
            }

            if (!_originalGamma.TryGetValue(target.DeviceName, out var original))
            {
                original = current.Clone();
                _originalGamma[target.DeviceName] = original;

                var knownFilter = DetectKnownExternalColorFilter();
                var externalRamp = IsMateriallyNonNeutral(current);
                if ((knownFilter is not null || externalRamp) && !forceSoftwareConflict)
                {
                    var source = knownFilter is not null
                        ? $"processo '{knownFilter}'"
                        : "transformação/calibração de cor já ativa";
                    return new(
                        request.ProfileId,
                        request.ProfileName,
                        TemperatureApplicationMethod.Software,
                        request.Kelvin,
                        null,
                        false,
                        true,
                        false,
                        $"Aplicação bloqueada: {source}. Ative a opção de forçar somente se quiser que o Wallpaper Scheduler também controle a saída.");
                }
            }
            else if (_lastGamma.TryGetValue(target.DeviceName, out var last) &&
                     !RampsClose(current, last) &&
                     !RampsClose(current, original) &&
                     !forceSoftwareConflict)
            {
                return new(
                    request.ProfileId,
                    request.ProfileName,
                    TemperatureApplicationMethod.Software,
                    request.Kelvin,
                    null,
                    false,
                    true,
                    false,
                    "Outra aplicação alterou a transformação de cor depois do Wallpaper Scheduler; controle suspenso para evitar disputa.");
            }

            var safeKelvin = Math.Clamp(request.Kelvin, 3400, 6500);
            var ramp = BuildTemperatureRamp(original, safeKelvin);
            if (!SetDeviceGammaRamp(hdc, ref ramp))
            {
                return new(
                    request.ProfileId,
                    request.ProfileName,
                    TemperatureApplicationMethod.Software,
                    request.Kelvin,
                    null,
                    false,
                    false,
                    false,
                    "O driver recusou a aplicação da transformação de cor.");
            }

            var verification = GammaRamp.Create();
            if (GetDeviceGammaRamp(hdc, ref verification) && !RampsClose(verification, ramp, tolerance: 1800))
            {
                logger.Warning($"Gamma ramp de '{target.DeviceName}' não corresponde integralmente ao valor solicitado; o driver pode ter limitado a transformação.");
            }

            _lastGamma[target.DeviceName] = ramp.Clone();
            return new(
                request.ProfileId,
                request.ProfileName,
                TemperatureApplicationMethod.Software,
                request.Kelvin,
                safeKelvin,
                true,
                false,
                false,
                safeKelvin == request.Kelvin
                    ? $"{safeKelvin}K aplicado por software."
                    : $"Solicitado {request.Kelvin}K; aplicado limite seguro de {safeKelvin}K pelo backend público de gamma ramp.");
        }
        finally
        {
            _ = DeleteDC(hdc);
        }
    }

    private TemperatureMonitorApplyStatus ApplyDdc(TemperatureMonitorRequest request, DisplayTarget target)
    {
        var physical = GetPhysicalMonitors(target);
        try
        {
            var applied = false;
            int? actualKelvin = null;
            foreach (var monitor in physical)
            {
                if (!GetMonitorCapabilities(monitor.Handle, out var capabilities, out var supported) ||
                    (capabilities & McCapsColorTemperature) == 0)
                    continue;

                var supportedKelvins = DecodeSupportedTemperatures(supported);
                if (supportedKelvins.Count == 0) continue;
                var nearest = supportedKelvins.OrderBy(x => Math.Abs(x - request.Kelvin)).First();
                var targetTemperature = KelvinToDdc(nearest);
                var key = PhysicalMonitorKey(target.DeviceName, monitor.Index);

                if (!_originalDdcTemperature.ContainsKey(key) && GetMonitorColorTemperature(monitor.Handle, out var current))
                    _originalDdcTemperature[key] = current;

                if (!SetMonitorColorTemperature(monitor.Handle, targetTemperature))
                    continue;

                applied = true;
                actualKelvin = nearest;
            }

            return new(
                request.ProfileId,
                request.ProfileName,
                TemperatureApplicationMethod.DdcCi,
                request.Kelvin,
                actualKelvin,
                applied,
                false,
                false,
                applied
                    ? actualKelvin == request.Kelvin
                        ? $"{actualKelvin}K aplicado diretamente pelo monitor via DDC/CI."
                        : $"DDC/CI aplicou o preset suportado mais próximo: {actualKelvin}K."
                    : "Este monitor não confirmou suporte funcional a temperatura por DDC/CI.");
        }
        finally
        {
            DestroyPhysicalMonitorList(physical);
        }
    }

    private void RestoreGammaRamps()
    {
        if (_originalGamma.Count == 0) return;
        foreach (var pair in _originalGamma.ToArray())
        {
            var hdc = CreateDC("DISPLAY", pair.Key, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) continue;
            try
            {
                var ramp = pair.Value.Clone();
                _ = SetDeviceGammaRamp(hdc, ref ramp);
            }
            finally { _ = DeleteDC(hdc); }
        }

        _originalGamma.Clear();
        _lastGamma.Clear();
        logger.Info("Transformações de cor por software restauradas ao estado anterior.");
    }

    private void RestoreDdcTemperatures()
    {
        if (_originalDdcTemperature.Count == 0) return;
        var targets = EnumerateDisplayTargets();
        foreach (var target in targets)
        {
            var physical = GetPhysicalMonitors(target);
            try
            {
                foreach (var monitor in physical)
                {
                    var key = PhysicalMonitorKey(target.DeviceName, monitor.Index);
                    if (_originalDdcTemperature.TryGetValue(key, out var temperature))
                        _ = SetMonitorColorTemperature(monitor.Handle, temperature);
                }
            }
            finally { DestroyPhysicalMonitorList(physical); }
        }

        _originalDdcTemperature.Clear();
        logger.Info("Temperaturas DDC/CI restauradas ao estado anterior.");
    }

    private static GammaRamp BuildTemperatureRamp(GammaRamp original, int kelvin)
    {
        var reference = TemperatureToRgb(6500);
        var target = TemperatureToRgb(kelvin);
        var redFactor = Math.Clamp(target.Red / reference.Red, 0.50, 1.0);
        var greenFactor = Math.Clamp(target.Green / reference.Green, 0.50, 1.0);
        var blueFactor = Math.Clamp(target.Blue / reference.Blue, 0.50, 1.0);

        var result = GammaRamp.Create();
        for (var i = 0; i < 256; i++)
        {
            result.Red[i] = Scale(original.Red[i], redFactor);
            result.Green[i] = Scale(original.Green[i], greenFactor);
            result.Blue[i] = Scale(original.Blue[i], blueFactor);
        }
        return result;
    }

    private static ushort Scale(ushort value, double factor) =>
        (ushort)Math.Clamp((int)Math.Round(value * factor), 0, ushort.MaxValue);

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

    private static bool IsMateriallyNonNeutral(GammaRamp ramp)
    {
        long totalDeviation = 0;
        for (var i = 0; i < 256; i++)
        {
            var identity = i * 257;
            totalDeviation += Math.Abs(ramp.Red[i] - identity);
            totalDeviation += Math.Abs(ramp.Green[i] - identity);
            totalDeviation += Math.Abs(ramp.Blue[i] - identity);
        }

        var meanDeviation = totalDeviation / (256.0 * 3.0);
        var endpointDeviation = Math.Max(
            Math.Abs(ramp.Red[255] - 65535),
            Math.Max(Math.Abs(ramp.Green[255] - 65535), Math.Abs(ramp.Blue[255] - 65535)));
        return meanDeviation > 1800 || endpointDeviation > 6000;
    }

    private static bool RampsClose(GammaRamp left, GammaRamp right, int tolerance = 900)
    {
        long total = 0;
        for (var i = 0; i < 256; i += 8)
        {
            total += Math.Abs(left.Red[i] - right.Red[i]);
            total += Math.Abs(left.Green[i] - right.Green[i]);
            total += Math.Abs(left.Blue[i] - right.Blue[i]);
        }
        return total / (32.0 * 3.0) <= tolerance;
    }

    private static string? DetectKnownExternalColorFilter()
    {
        string[] processNames = ["flux", "LightBulb", "SunsetScreen", "Iris"];
        foreach (var name in processNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) return name;
            }
            catch { }
        }
        return null;
    }

    private static List<int> GetDdcSupportedTemperatures(DisplayTarget target)
    {
        var physical = GetPhysicalMonitors(target);
        try
        {
            var values = new HashSet<int>();
            foreach (var monitor in physical)
            {
                if (!GetMonitorCapabilities(monitor.Handle, out var capabilities, out var supported) ||
                    (capabilities & McCapsColorTemperature) == 0)
                    continue;
                foreach (var kelvin in DecodeSupportedTemperatures(supported)) values.Add(kelvin);
            }
            return values.Order().ToList();
        }
        finally { DestroyPhysicalMonitorList(physical); }
    }

    private static List<int> DecodeSupportedTemperatures(uint flags)
    {
        var result = new List<int>();
        if ((flags & 0x01) != 0) result.Add(4000);
        if ((flags & 0x02) != 0) result.Add(5000);
        if ((flags & 0x04) != 0) result.Add(6500);
        if ((flags & 0x08) != 0) result.Add(7500);
        if ((flags & 0x10) != 0) result.Add(8200);
        if ((flags & 0x20) != 0) result.Add(9300);
        if ((flags & 0x40) != 0) result.Add(10000);
        if ((flags & 0x80) != 0) result.Add(11500);
        return result;
    }

    private static McColorTemperature KelvinToDdc(int kelvin) => kelvin switch
    {
        <= 4000 => McColorTemperature.K4000,
        <= 5000 => McColorTemperature.K5000,
        <= 6500 => McColorTemperature.K6500,
        <= 7500 => McColorTemperature.K7500,
        <= 8200 => McColorTemperature.K8200,
        <= 9300 => McColorTemperature.K9300,
        <= 10000 => McColorTemperature.K10000,
        _ => McColorTemperature.K11500
    };

    private static string PhysicalMonitorKey(string deviceName, int index) => $"{deviceName}|{index}";

    private static List<PhysicalMonitorRef> GetPhysicalMonitors(DisplayTarget target)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(target.HMonitor, out var count) || count == 0)
            return [];

        var native = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(target.HMonitor, count, native))
            return [];

        var result = new List<PhysicalMonitorRef>((int)count);
        for (var i = 0; i < native.Length; i++)
            result.Add(new(native[i].Handle, native[i].Description ?? string.Empty, i, native));
        return result;
    }

    private static void DestroyPhysicalMonitorList(List<PhysicalMonitorRef> monitors)
    {
        if (monitors.Count == 0) return;
        var native = monitors[0].OwnerArray;
        if (native.Length > 0)
            _ = DestroyPhysicalMonitors((uint)native.Length, native);
    }

    private static List<DisplayTarget> EnumerateDisplayTargets()
    {
        var result = new List<DisplayTarget>();
        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(hMonitor, ref info)) return true;
            var width = info.Monitor.Right - info.Monitor.Left;
            var height = info.Monitor.Bottom - info.Monitor.Top;
            result.Add(new(hMonitor, info.DeviceName ?? string.Empty, info.Monitor.Left, info.Monitor.Top, width, height));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static DisplayTarget? FindTarget(MonitorInfo monitor, IReadOnlyList<DisplayTarget> targets)
    {
        var exact = targets.FirstOrDefault(x =>
            x.Left == monitor.Left && x.Top == monitor.Top && x.Width == monitor.Width && x.Height == monitor.Height);
        if (exact is not null) return exact;

        var sizeMatches = targets.Where(x => x.Width == monitor.Width && x.Height == monitor.Height).ToList();
        return sizeMatches.Count == 1 ? sizeMatches[0] : null;
    }

    private static Dictionary<string, bool> GetAdvancedColorStateByDevice()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess)
            return result;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var currentPathCount = pathCount;
            var currentModeCount = modeCount;
            var error = QueryDisplayConfig(
                QdcOnlyActivePaths,
                ref currentPathCount,
                paths,
                ref currentModeCount,
                modes,
                IntPtr.Zero);

            if (error == 122)
            {
                if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount) != ErrorSuccess)
                    return result;
                continue;
            }
            if (error != ErrorSuccess) return result;

            foreach (var path in paths.Take((int)currentPathCount))
            {
                var source = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigDeviceInfoType.GetSourceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref source) != ErrorSuccess || string.IsNullOrWhiteSpace(source.ViewGdiDeviceName))
                    continue;

                var color = new DisplayConfigGetAdvancedColorInfo
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo,
                        Size = (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref color) == ErrorSuccess)
                    result[source.ViewGdiDeviceName] = (color.Value & 0x2) != 0;
            }
            break;
        }

        return result;
    }

    private sealed record DisplayTarget(IntPtr HMonitor, string DeviceName, int Left, int Top, int Width, int Height);
    private sealed record RgbTemperature(double Red, double Green, double Blue);
    private sealed record PhysicalMonitorRef(IntPtr Handle, string Description, int Index, PhysicalMonitor[] OwnerArray);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;

        public static GammaRamp Create() => new()
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        public readonly GammaRamp Clone() => new()
        {
            Red = (ushort[])Red.Clone(),
            Green = (ushort[])Green.Clone(),
            Blue = (ushort[])Blue.Clone()
        };
    }

    private enum McColorTemperature
    {
        Unknown,
        K4000,
        K5000,
        K6500,
        K7500,
        K8200,
        K9300,
        K10000,
        K11500
    }

    private enum DisplayConfigDeviceInfoType : uint
    {
        GetSourceName = 1,
        GetAdvancedColorInfo = 9
    }

    private enum DisplayConfigColorEncoding : uint
    {
        Rgb,
        YCbCr444,
        YCbCr422,
        YCbCr420,
        Intensity
    }

    private enum DisplayConfigScaling : uint { Identity = 1, Centered, Stretched, AspectRatioCenteredMax, Custom, Preferred = 128 }
    private enum DisplayConfigRotation : uint { Identity = 1, Rotate90, Rotate180, Rotate270 }
    private enum DisplayConfigVideoOutputTechnology : int { Other = -1 }
    private enum DisplayConfigScanlineOrdering : uint { Unspecified, Progressive, Interlaced, InterlacedLowerFieldFirst = 3 }
    private enum DisplayConfigPixelFormat : uint { Bpp8 = 1, Bpp16, Bpp24, Bpp32, NonGdi }
    private enum DisplayConfigModeInfoType : uint { Source = 1, Target, DesktopImage }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion { public uint Cx; public uint Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public DisplayConfigPixelFormat PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public DisplayConfigScanlineOrdering ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode { public DisplayConfigVideoSignalInfo TargetVideoSignalInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public Rect DesktopImageRegion;
        public Rect DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
        [FieldOffset(0)] public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public DisplayConfigRotation Rotation;
        public DisplayConfigScaling Scaling;
        public DisplayConfigRational RefreshRate;
        public DisplayConfigScanlineOrdering ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public DisplayConfigModeInfoType InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion Info;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public DisplayConfigColorEncoding ColorEncoding;
        public uint BitsPerColorChannel;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx monitorInfo);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint numberOfPhysicalMonitors);

    [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint physicalMonitorArraySize, [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint physicalMonitorArraySize, [In] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorCapabilities(IntPtr hMonitor, out uint monitorCapabilities, out uint supportedColorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorColorTemperature(IntPtr hMonitor, out McColorTemperature currentColorTemperature);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorColorTemperature(IntPtr hMonitor, McColorTemperature colorTemperature);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [In, Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [In, Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo requestPacket);
}
