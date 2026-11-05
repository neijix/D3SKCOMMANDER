using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Tests;

/// <summary>
/// The app's real composition for a test, but no test ever starts a real program: a
/// double-click on a file, Open with or a user command reach a runner that only counts.
/// </summary>
internal static class TestComposition
{
    public static ServiceProvider Build(ILocalizer? localizer = null, Action<IServiceCollection>? configure = null) =>
        Program.BuildProvider(localizer, services =>
        {
            services.AddSingleton<IProcessRunner>(new NoProcesses());
            configure?.Invoke(services);
        });

    /// <summary>Counts the requests and starts nothing.</summary>
    internal sealed class NoProcesses : IProcessRunner
    {
        public int Requests { get; private set; }

        public void Start(ProcessRequest request) => Requests++;
    }
}
