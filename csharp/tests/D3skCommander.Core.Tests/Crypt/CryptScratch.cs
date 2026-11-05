namespace D3skCommander.Core.Tests.Crypt;

internal sealed class CryptScratch : IDisposable
{
    public CryptScratch()
    {
        Folder = Directory.CreateTempSubdirectory("d3sk-crypt-").FullName;
    }

    public string Folder { get; }

    public void Dispose()
    {
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }
    }
}
