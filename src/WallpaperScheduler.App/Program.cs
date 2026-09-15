using System;
using FormsApplication = System.Windows.Forms.Application;
using FormsHighDpiMode = System.Windows.Forms.HighDpiMode;

namespace WallpaperScheduler.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        FormsApplication.SetHighDpiMode(FormsHighDpiMode.PerMonitorV2);
        RenderingBootstrap.Configure();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
