using System.Diagnostics;
using System.Security.Cryptography;
using Vitals.Sensors;

namespace Vitals.Setup;

internal enum PawnIoSetupResult
{
    AlreadyInstalled,
    Installed,
    RebootRequired,
    Failed,
}

internal sealed class PawnIoSetupOptions
{
    public bool Force { get; init; }

    public bool Debug { get; init; }

    public TextWriter Output { get; init; } = Console.Out;
}

internal readonly record struct InstallerResult(int ExitCode, string Output);

internal interface IPawnIoSetupEnvironment
{
    bool IsInstalled();

    bool IsElevated();

    string CreateTempPath();

    Task<Stream> OpenDownloadAsync(CancellationToken cancellationToken);

    Task<InstallerResult> RunInstallerAsync(string path, bool elevated, CancellationToken cancellationToken);

    void Delete(string path);
}

/// <summary>
/// Downloads the official PawnIO installer, verifies a pinned SHA-256 and runs
/// it with the vendor's silent switches. PawnIO is never bundled with vitals.
/// The caller is responsible for obtaining consent before calling this.
/// </summary>
internal static class PawnIoSetup
{
    public const string Version = "2.2.0";

    public const string DownloadUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";

    public const string Sha256 =
        "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    public const long MaxDownloadBytes = 8 * 1024 * 1024;

    public static PawnIoSetupResult EnsureInstalled(PawnIoSetupOptions options) =>
        EnsureInstalledAsync(options, PawnIoSetupEnvironment.Default).GetAwaiter().GetResult();

    internal static async Task<PawnIoSetupResult> EnsureInstalledAsync(
        PawnIoSetupOptions options,
        IPawnIoSetupEnvironment environment,
        string? expectedSha256 = null,
        long maxDownloadBytes = MaxDownloadBytes,
        CancellationToken cancellationToken = default)
    {
        var output = options.Output;
        expectedSha256 ??= Sha256;

        if (!options.Force && environment.IsInstalled())
        {
            output.WriteLine("PawnIO is already installed.");
            return PawnIoSetupResult.AlreadyInstalled;
        }

        string? path = null;
        try
        {
            path = environment.CreateTempPath();
            output.WriteLine($"Downloading PawnIO {Version} from GitHub...");
            if (options.Debug)
            {
                output.WriteLine($"  url: {DownloadUrl}");
                output.WriteLine($"  expected sha256: {expectedSha256}");
            }

            using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                downloadTimeout.Token);
            var downloadToken = downloadCancellation.Token;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await environment.OpenDownloadAsync(downloadToken))
            await using (var destination = new FileStream(
                             path,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), downloadToken)) > 0)
                {
                    total += read;
                    if (total > maxDownloadBytes)
                    {
                        throw new InvalidDataException(
                            $"installer is larger than the {maxDownloadBytes} byte limit");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), downloadToken);
                }
            }

            var actualSha256 = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"downloaded installer hash {actualSha256} does not match {expectedSha256}");
            }

            output.WriteLine("SHA-256 verified. Installing PawnIO...");
            var elevated = environment.IsElevated();
            var result = await environment.RunInstallerAsync(path, elevated, cancellationToken);

            if (options.Debug)
            {
                output.WriteLine($"  installer exit code: {result.ExitCode}");
                if (result.Output.Length > 0)
                {
                    output.WriteLine($"  installer output: {result.Output}");
                }
            }

            if (result.ExitCode is 3010 or 1641)
            {
                output.WriteLine("PawnIO was installed. Windows must restart before the driver is active.");
                return PawnIoSetupResult.RebootRequired;
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"installer exited with code {result.ExitCode}" +
                    (result.Output.Length > 0 ? $": {result.Output}" : string.Empty));
            }

            if (!environment.IsInstalled())
            {
                throw new InvalidOperationException(
                    "installer reported success but PawnIO was not detected");
            }

            output.WriteLine($"PawnIO {Version} installed.");
            return PawnIoSetupResult.Installed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("vitals: PawnIO setup failed: the download timed out.");
            return PawnIoSetupResult.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"vitals: PawnIO setup failed: {ex.Message}");
            if (options.Debug)
            {
                output.WriteLine(ex);
            }

            return PawnIoSetupResult.Failed;
        }
        finally
        {
            if (path is not null)
            {
                environment.Delete(path);
            }
        }
    }
}

internal sealed class PawnIoSetupEnvironment : IPawnIoSetupEnvironment
{
    public static readonly PawnIoSetupEnvironment Default = new();

    private static readonly HttpClient Client = CreateClient();

    private PawnIoSetupEnvironment()
    {
    }

    public bool IsInstalled() => LhmSource.IsPawnIoInstalled();

    public bool IsElevated() => LhmSource.IsProcessElevated();

    public string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"vitals-pawnio-{PawnIoSetup.Version}-{Guid.NewGuid():N}.exe");

    public async Task<Stream> OpenDownloadAsync(CancellationToken cancellationToken)
    {
        // Follow redirects manually so every hop is checked for HTTPS. The
        // pinned URL redirects to GitHub's object host, and a silent downgrade
        // to plain HTTP must never be accepted.
        var uri = new Uri(PawnIoSetup.DownloadUrl);
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException(
                    $"refusing to download the PawnIO installer over {uri.Scheme}");
            }

            var response = await Client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 &&
                response.Headers.Location is { } location)
            {
                response.Dispose();
                uri = new Uri(uri, location);
                continue;
            }

            try
            {
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return new ResponseStream(response, stream);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        throw new InvalidDataException("too many redirects while downloading the PawnIO installer");
    }

    public async Task<InstallerResult> RunInstallerAsync(
        string path,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = !elevated,
            CreateNoWindow = elevated,
        };
        start.ArgumentList.Add("-install");
        start.ArgumentList.Add("-silent");

        if (elevated)
        {
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
        }
        else
        {
            start.Verb = "runas";
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("could not start the PawnIO installer");

        var outputTask = elevated
            ? ReadOutputAsync(process, cancellationToken)
            : Task.FromResult(string.Empty);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the timeout and the kill.
            }

            throw;
        }

        var output = await outputTask;
        return new InstallerResult(process.ExitCode, output);
    }

    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A locked temp file is harmless; Windows cleans %TEMP% eventually.
        }
    }

    private static async Task<string> ReadOutputAsync(Process process, CancellationToken cancellationToken)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        return output.Trim();
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"vitals/{AppInfo.Version}");
        return client;
    }

    private sealed class ResponseStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public ResponseStream(HttpResponseMessage response, Stream inner)
        {
            _response = response;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    _response.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await _inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _response.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
