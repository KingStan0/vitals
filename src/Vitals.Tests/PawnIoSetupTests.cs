using System.Security.Cryptography;
using Vitals.Setup;

namespace Vitals.Tests;

public sealed class PawnIoSetupTests
{
    private static readonly byte[] InstallerBytes = { 0x4D, 0x5A, 0x01, 0x02, 0x03 };

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void Pinned_installer_metadata_is_sane()
    {
        Assert.StartsWith("https://", PawnIoSetup.DownloadUrl, StringComparison.Ordinal);
        Assert.Contains(PawnIoSetup.Version, PawnIoSetup.DownloadUrl, StringComparison.Ordinal);
        Assert.Equal(64, PawnIoSetup.Sha256.Length);
        Assert.True(PawnIoSetup.MaxDownloadBytes > 3_410_960);
    }

    [Fact]
    public async Task Already_installed_skips_download_and_installer()
    {
        var environment = new FakeEnvironment { Installed = true };

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = TextWriter.Null },
            environment);

        Assert.Equal(PawnIoSetupResult.AlreadyInstalled, result);
        Assert.Equal(0, environment.DownloadCount);
        Assert.Equal(0, environment.RunCount);
    }

    [Fact]
    public async Task Force_reinstalls_even_when_already_installed()
    {
        var environment = new FakeEnvironment
        {
            Installed = true,
            Download = InstallerBytes,
        };

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Force = true, Output = TextWriter.Null },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.Installed, result);
        Assert.Equal(1, environment.DownloadCount);
        Assert.Equal(1, environment.RunCount);
    }

    [Fact]
    public async Task Hash_mismatch_does_not_run_the_installer()
    {
        var environment = new FakeEnvironment { Download = InstallerBytes };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment,
            new string('0', 64));

        Assert.Equal(PawnIoSetupResult.Failed, result);
        Assert.Equal(0, environment.RunCount);
        Assert.Contains("does not match", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(environment.TempPath, environment.Deleted);
    }

    [Fact]
    public async Task Success_runs_the_installer_and_deletes_the_temp_file()
    {
        var environment = new FakeEnvironment
        {
            Download = InstallerBytes,
            Elevated = true,
        };

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = TextWriter.Null },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.Installed, result);
        Assert.Equal(1, environment.RunCount);
        Assert.Equal(environment.TempPath, environment.LastPath);
        Assert.True(environment.LastElevated);
        Assert.Contains(environment.TempPath, environment.Deleted);
    }

    [Fact]
    public async Task Non_elevated_process_uses_the_runas_installer_path()
    {
        var environment = new FakeEnvironment
        {
            Download = InstallerBytes,
            Elevated = false,
        };

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = TextWriter.Null },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.Installed, result);
        Assert.False(environment.LastElevated);
    }

    [Fact]
    public async Task Reboot_required_is_reported_as_success()
    {
        var environment = new FakeEnvironment
        {
            Download = InstallerBytes,
            ExitCode = 3010,
            InstallMarksInstalled = false,
        };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.RebootRequired, result);
        Assert.Contains("restart", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Installer_failure_includes_the_exit_code()
    {
        var environment = new FakeEnvironment
        {
            Download = InstallerBytes,
            ExitCode = 5,
            InstallMarksInstalled = false,
        };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.Failed, result);
        Assert.Contains("code 5", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installer_success_without_detection_is_a_failure()
    {
        var environment = new FakeEnvironment
        {
            Download = InstallerBytes,
            ExitCode = 0,
            InstallMarksInstalled = false,
        };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment,
            HashOf(InstallerBytes));

        Assert.Equal(PawnIoSetupResult.Failed, result);
        Assert.Contains("not detected", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_download_is_rejected()
    {
        var environment = new FakeEnvironment { Download = new byte[10] };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment,
            HashOf(environment.Download),
            maxDownloadBytes: 4);

        Assert.Equal(PawnIoSetupResult.Failed, result);
        Assert.Equal(0, environment.RunCount);
        Assert.Contains("byte limit", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_failure_is_reported()
    {
        var environment = new FakeEnvironment
        {
            DownloadError = new HttpRequestException("network down"),
        };
        var output = new StringWriter();

        var result = await PawnIoSetup.EnsureInstalledAsync(
            new PawnIoSetupOptions { Output = output },
            environment);

        Assert.Equal(PawnIoSetupResult.Failed, result);
        Assert.Contains("network down", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeEnvironment : IPawnIoSetupEnvironment
    {
        public bool Installed { get; set; }

        public bool Elevated { get; set; } = true;

        public byte[] Download { get; set; } = Array.Empty<byte>();

        public Exception? DownloadError { get; set; }

        public int ExitCode { get; set; }

        public bool InstallMarksInstalled { get; set; } = true;

        public string TempPath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"vitals-pawnio-test-{Guid.NewGuid():N}.exe");

        public List<string> Deleted { get; } = new();

        public int DownloadCount { get; private set; }

        public int RunCount { get; private set; }

        public bool? LastElevated { get; private set; }

        public string? LastPath { get; private set; }

        public bool IsInstalled() => Installed;

        public bool IsElevated() => Elevated;

        public string CreateTempPath() => TempPath;

        public Task<Stream> OpenDownloadAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            if (DownloadError is not null)
            {
                throw DownloadError;
            }

            return Task.FromResult<Stream>(new MemoryStream(Download, writable: false));
        }

        public Task<InstallerResult> RunInstallerAsync(
            string path,
            bool elevated,
            CancellationToken cancellationToken)
        {
            RunCount++;
            LastPath = path;
            LastElevated = elevated;
            if (ExitCode == 0 && InstallMarksInstalled)
            {
                Installed = true;
            }

            return Task.FromResult(new InstallerResult(ExitCode, string.Empty));
        }

        public void Delete(string path)
        {
            Deleted.Add(path);
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Tests do not need to fail on cleanup.
            }
        }
    }
}
