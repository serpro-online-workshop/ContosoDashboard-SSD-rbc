using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ContosoDashboard.Tests.TestInfrastructure;

public sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "ContosoDashboard.Tests";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }

    public TestHostEnvironment(string contentRoot)
    {
        ContentRootPath = contentRoot;
        ContentRootFileProvider = new PhysicalFileProvider(contentRoot);
    }
}