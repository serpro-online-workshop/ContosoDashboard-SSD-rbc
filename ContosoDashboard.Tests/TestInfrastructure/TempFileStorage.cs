using ContosoDashboard.Services.Storage;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests.TestInfrastructure;

public sealed class TempFileStorage : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ContosoDashboard.Tests", Guid.NewGuid().ToString("N"));

    public LocalFileStorageService Storage { get; }
    public string RootPath => _root;

    public TempFileStorage()
    {
        Directory.CreateDirectory(_root);
        var environment = new TestHostEnvironment(_root);
        Storage = new LocalFileStorageService(Options.Create(new FileStorageOptions { RootPath = _root }), environment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}