using System.Reflection;

namespace D3skCommander.Core.Update;

/// <summary>
/// Version and repository URL from the assembly metadata
/// <c>Directory.Build.props</c> writes (<c>Version</c>, <c>RepositoryUrl</c>).
/// </summary>
public static class UpdateMetadata
{
    private static string? _version;
    private static string? _repository;

    public static string CurrentVersion => _version ??= ReadVersion();

    public static string RepositoryUrl => _repository ??= ReadRepository();

    private static string ReadVersion()
    {
        var info = ThisAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var text = info?.InformationalVersion ?? "";
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? text : text[..plus];
    }

    private static string ReadRepository()
    {
        foreach (var attribute in ThisAssembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == "RepositoryUrl" && !string.IsNullOrEmpty(attribute.Value))
            {
                return attribute.Value;
            }
        }

        return "";
    }

    private static Assembly ThisAssembly => typeof(UpdateMetadata).Assembly;
}
