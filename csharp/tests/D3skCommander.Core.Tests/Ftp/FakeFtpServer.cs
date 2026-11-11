using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.FileSystem.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>
/// The in-process FTP server (FubarDev) on 127.0.0.1 and a free port, serving a folder on disk.
/// One account, <see cref="User"/> and <see cref="Password"/>. With a certificate it also speaks
/// explicit FTPS. <see cref="Dispose"/> stops it.
/// </summary>
public sealed class FakeFtpServer : IDisposable
{
    public const string User = "tester";
    public const string Password = "s3cret";

    private readonly IServiceProvider _services;
    private readonly IFtpServerHost _host;

    private FakeFtpServer(string root, int port, IServiceProvider services, IFtpServerHost host)
    {
        Root = root;
        Port = port;
        _services = services;
        _host = host;
    }

    public string Root { get; }

    public int Port { get; }

    public static FakeFtpServer Start(string root, X509Certificate2? certificate = null, bool withMlsd = true)
    {
        var port = FreePort();
        var services = new ServiceCollection();
        services.AddFtpServer(builder => builder.UseDotNetFileSystem());
        services.AddSingleton<IMembershipProvider, FixedMembership>();
        services.Configure<DotNetFileSystemOptions>(options => options.RootPath = root);
        services.Configure<FtpServerOptions>(options =>
        {
            options.ServerAddress = "127.0.0.1";
            options.Port = port;
        });
        services.Configure<SimplePasvOptions>(options =>
        {
            options.PasvMinPort = 0;
            options.PasvMaxPort = 0;
        });
        if (!withMlsd)
        {
            services.RemoveAll<IFtpCommandHandlerScanner>();
            services.AddSingleton<IFtpCommandHandlerScanner, NoMlsdScanner>();
        }

        if (certificate is not null)
        {
            services.Configure<AuthTlsOptions>(options => options.ServerCertificate = certificate);
        }

        var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IFtpServerHost>();
        host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new FakeFtpServer(root, port, provider, host);
    }

    public void Dispose()
    {
        _host.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        (_services as IDisposable)?.Dispose();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The server's own commands without MLSD, as vsftpd has none: clients have to fall back to LIST.</summary>
    private sealed class NoMlsdScanner : IFtpCommandHandlerScanner
    {
        public IEnumerable<IFtpCommandHandlerInformation> Search() =>
            new AssemblyFtpCommandHandlerScanner(
                typeof(FubarDev.FtpServer.FtpServer).Assembly,
                typeof(FubarDev.FtpServer.CommandHandlers.ListCommandHandler).Assembly)
                .Search()
                .Where(info => info.Name != "MLSD");
    }

    private sealed class FixedMembership : IMembershipProvider
    {
        public Task<MemberValidationResult> ValidateUserAsync(string username, string password)
        {
            if (username == User && password == Password)
            {
                var identity = new ClaimsIdentity("test");
                identity.AddClaim(new Claim(ClaimTypes.Name, username));
                return Task.FromResult(new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, new ClaimsPrincipal(identity)));
            }

            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }
    }
}
