using System.IO;
using SynRdpManager.App.Platform;
using SynRdpManager.Core.Catalog;
using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Putty;
using SynRdpManager.Core.Rdp;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.Services;

/// <summary>
/// Composed services plus everything derived from the current settings (tool paths, catalog, PuTTY manager).
/// Built in <see cref="App"/>; <see cref="Apply"/> re-derives after the settings change.
/// </summary>
public sealed class AppServices
{
    public AppServices(SettingsLoadResult load, RdpLauncher rdp, IProcessLauncher launcher,
        IDataProtector protector, IPuttySessionStore puttyStore)
    {
        Rdp = rdp;
        Launcher = launcher;
        Protector = protector;
        PuttyStore = puttyStore;
        Apply(load);
    }

    public RdpLauncher Rdp { get; }
    public IProcessLauncher Launcher { get; }
    public IDataProtector Protector { get; }
    public IPuttySessionStore PuttyStore { get; }

    public SettingsLoadResult Load { get; private set; } = null!; // set by Apply in the constructor
    public AppSettings Settings => Load.Effective;
    public string? OpPath { get; private set; }
    public string? PuttyPath { get; private set; }

    /// <summary>Null when op.exe was not found: no OpClient calls are made then.</summary>
    public CatalogService? Catalog { get; private set; }

    public PuttySessionManager Putty { get; private set; } = null!; // set by Apply in the constructor

    public void Apply(SettingsLoadResult load)
    {
        Load = load;
        var s = load.Effective;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        OpPath = ExeLocator.Find("op.exe", s.OpPath,
            [AppContext.BaseDirectory, Path.Combine(programFiles, "1Password CLI")], pathEnv, File.Exists);
        PuttyPath = ExeLocator.Find("putty.exe", s.PuttyPath,
            [Path.Combine(programFiles, "PuTTY")], pathEnv, File.Exists);
        Catalog = OpPath is null ? null : new CatalogService(new OpClient(OpPath, new ProcessRunner()));
        ResetPutty();
    }

    public void ResetPutty() => Putty = new PuttySessionManager(PuttyStore, Settings.PuttyTemplateSession);
}
