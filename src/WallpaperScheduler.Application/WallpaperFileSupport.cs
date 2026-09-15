namespace WallpaperScheduler.Application;

public static class WallpaperFileSupport
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    public const string DisplayNames = "JPG/JPEG, PNG e BMP";

    public static bool IsSupportedExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsSupportedExistingFile(string path) =>
        File.Exists(path) && IsSupportedExtension(path);
}
