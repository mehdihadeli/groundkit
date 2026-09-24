using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GroundKit.Cli;
using GroundKit.Configuration;
using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using GroundKit.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundKit.Tests.Integration;

public sealed class AddFromDirectoryIntegrationTests : IDisposable
{
    private const string RepositoryUrl = "https://github.com/mattpocock/skills";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"groundkit-directory-add-{Guid.NewGuid():N}"
    );
    private readonly string _repositoryPath;
    private readonly string _packageRoot;

    public AddFromDirectoryIntegrationTests()
    {
        _repositoryPath = Path.Combine(_root, "skills");
        _packageRoot = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_root);
        try
        {
            CloneRepository();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Should_Remove_Cloned_Repository_When_Disposed()
    {
        Directory.Exists(_repositoryPath).ShouldBeTrue();

        Dispose();

        Directory.Exists(_root).ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Local_Repository_And_Auto_Detect_Docs_Folder()
    {
        var application = CreateApplication("auto-detect");

        var exitCode = await application.RunAsync(["add", _repositoryPath]);

        var packageStore = CreatePackageStore("auto-detect");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.Kind.ShouldBe(SourceKind.LocalDirectory);
        source.Location.ShouldBe(Path.GetFullPath(_repositoryPath));
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Local_Repository_With_Explicit_Path()
    {
        var application = CreateApplication("explicit-path");

        var exitCode = await application.RunAsync(["add", _repositoryPath, "--path", "docs"]);

        var packageStore = CreatePackageStore("explicit-path");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.DocsPath.ShouldBe("docs");
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Local_Repository_With_Custom_Name_And_Version()
    {
        var application = CreateApplication("custom-metadata");

        var exitCode = await application.RunAsync(
            [
                "add",
                _repositoryPath,
                "--path",
                "docs",
                "--name",
                "my-library",
                "--pkg-version",
                "1.0.0",
            ]
        );

        var packageStore = CreatePackageStore("custom-metadata");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("my-library");
        package.Version.ShouldBe("1.0.0");
        source.ShouldNotBeNull();
        source.CanonicalId.ShouldBe("my-library");
        source.DisplayName.ShouldBe("my-library");
        source.Version.ShouldBe("1.0.0");
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Save_Local_Package_Copy_For_Sharing()
    {
        var application = CreateApplication("saved-copy");
        var savedCopyPath = Path.Combine(_root, "shared", "skills@1.0.0.db");

        var exitCode = await application.RunAsync(
            [
                "add",
                _repositoryPath,
                "--path",
                "docs",
                "--name",
                "mattpocock-skills",
                "--pkg-version",
                "1.0.0",
                "--save",
                savedCopyPath,
            ]
        );

        exitCode.ShouldBe(0);
        File.Exists(savedCopyPath).ShouldBeTrue();
        new FileInfo(savedCopyPath).Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Local_Saved_Database_File()
    {
        var savedCopyPath = Path.Combine(_root, "mattpocock-skills@1.2.3.db");
        var sourceExitCode = await CreateApplication("local-file-source")
            .RunAsync(
                [
                    "add",
                    _repositoryPath,
                    "--path",
                    "docs",
                    "--name",
                    "mattpocock-skills",
                    "--pkg-version",
                    "1.2.3",
                    "--save",
                    savedCopyPath,
                ]
            );

        sourceExitCode.ShouldBe(0);
        File.Exists(savedCopyPath).ShouldBeTrue();

        var destinationStore = CreatePackageStore("local-file-destination");
        var destinationBuilder = new DocumentPackageBuilder(
            new SourceDetector(),
            new EmptyHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var downloader = new PackageDownloadService(
            null!,
            destinationStore,
            destinationBuilder,
            new DefaultHttpClientFactory(),
            new GroundKitOptions(),
            NullLogger<PackageDownloadService>.Instance
        );
        var destinationApplication = new CliApplication(
            destinationBuilder,
            destinationStore,
            packageDownloadService: downloader
        );

        var exitCode = await destinationApplication.RunAsync(["add", savedCopyPath]);
        var package = (
            await destinationStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("mattpocock-skills");
        package.Version.ShouldBe("1.2.3");
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Package_From_Locally_Hosted_Database_Url()
    {
        var savedCopyPath = Path.Combine(_root, "shared", "mattpocock-skills@1.2.3.db");
        var sourceApplication = CreateApplication("host-source");
        var sourceExitCode = await sourceApplication.RunAsync(
            [
                "add",
                _repositoryPath,
                "--path",
                "docs",
                "--name",
                "mattpocock-skills",
                "--pkg-version",
                "1.2.3",
                "--save",
                savedCopyPath,
            ]
        );

        sourceExitCode.ShouldBe(0);
        File.Exists(savedCopyPath).ShouldBeTrue();

        using var server = new LocalPackageServer(savedCopyPath);
        var destinationStore = CreatePackageStore("host-destination");
        var destinationBuilder = new DocumentPackageBuilder(
            new SourceDetector(),
            new EmptyHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var downloader = new PackageDownloadService(
            null!,
            destinationStore,
            destinationBuilder,
            new DefaultHttpClientFactory(),
            new GroundKitOptions(),
            NullLogger<PackageDownloadService>.Instance
        );
        var destinationApplication = new CliApplication(
            destinationBuilder,
            destinationStore,
            packageDownloadService: downloader
        );
        var servingTask = server.ServeOnceAsync(TestContext.Current.CancellationToken);

        var exitCode = await destinationApplication.RunAsync(["add", server.Url]);
        await servingTask;

        var package = (
            await destinationStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("mattpocock-skills");
        package.Version.ShouldBe("1.2.3");
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    private CliApplication CreateApplication(string scenario)
    {
        return new CliApplication(
            new DocumentPackageBuilder(
                new SourceDetector(),
                new EmptyHttpClientFactory(),
                NullLogger<DocumentPackageBuilder>.Instance
            ),
            CreatePackageStore(scenario)
        );
    }

    private SqlitePackageStore CreatePackageStore(string scenario)
    {
        return new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_packageRoot, scenario)),
            NullLogger<SqlitePackageStore>.Instance
        );
    }

    private void CloneRepository()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("clone");
        startInfo.ArgumentList.Add("--depth");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add(RepositoryUrl);
        startInfo.ArgumentList.Add(_repositoryPath);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git clone.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Could not clone test repository: {error}");
        }
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class LocalPackageServer : IDisposable
    {
        private readonly string packagePath;
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public LocalPackageServer(string packagePath)
        {
            this.packagePath = packagePath;
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/mattpocock-skills@1.2.3";
        }

        public string Url { get; }

        public async Task ServeOnceAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var request = new StringBuilder();
            var buffer = new byte[1024];
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var length = new FileInfo(packagePath).Length;
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n"
            );
            await stream.WriteAsync(headers, cancellationToken);
            await using var package = File.OpenRead(packagePath);
            await package.CopyToAsync(stream, cancellationToken);
        }

        public void Dispose() => listener.Stop();
    }
}
