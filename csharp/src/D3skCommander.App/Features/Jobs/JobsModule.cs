using Avalonia.Controls;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Jobs;

public sealed class JobsModule : IShellModule
{
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;

    public JobsModule(IJobService jobs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        _jobs = jobs;
        _text = text;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IJobService, JobService>();
        services.AddSingleton<JobsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<JobsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.AddRegion(ShellRegion.UnderPanels, () => new OperationsBarWidget(_jobs, _text));
    }

    private sealed class OperationsBarWidget(IJobService jobs, ILocalizer text) : IShellWidget
    {
        public string? Name => "Operations";

        public void Draw()
        {
        }

        public Control CreateContent() => new OperationsBarView(new OperationsBarModel(jobs, text)) { Name = Name };
    }
}
