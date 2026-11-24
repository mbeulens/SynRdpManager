using Velopack;

namespace SynRdpManager.App;

/// <summary>Entry point: Velopack handles install/update hooks first (it may exit the process), then WPF starts.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
