namespace D3skCommander.App.Shell;

/// <summary>
/// A feature's hook into the shell. <c>Register</c> on the module class adds services;
/// the shell calls <see cref="Contribute"/> for each registered module. Do not inject <see cref="MainWindow"/>
/// (it is what calls this). Strings are embedded JSON catalogs, which the localizer already loads.
/// </summary>
public interface IShellModule
{
    void Contribute(IShellContributions shell);
}
