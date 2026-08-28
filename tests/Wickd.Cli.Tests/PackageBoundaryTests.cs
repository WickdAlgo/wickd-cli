using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;

namespace Wickd.Cli.Tests;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void AnAcronymCasedCcxtAssemblyIsRefused()
    {
        using var workspace = new TempWorkspace();
        var package = Path.Combine(workspace.Root, "wickd-cli.0.0.0.nupkg");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            zip.CreateEntry("lib/net10.0/CCXT.dll");
        }

        var result = RunVerify(package);

        result.ExitCode.Should().Be(1);
        result.Error.Should().Contain("proprietary engine or exchange-adapter content");
    }

    [Fact]
    public void ACleanPackageIsAccepted()
    {
        using var workspace = new TempWorkspace();
        var package = Path.Combine(workspace.Root, "wickd-cli.0.0.0.nupkg");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            zip.CreateEntry("lib/net10.0/Wickd.Cli.dll");
        }

        var result = RunVerify(package);

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("clean package boundary");
    }

    private static (int ExitCode, string Output, string Error) RunVerify(string package)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sh",
            ArgumentList = { VerifyScript(), package },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start verify-package.sh.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000).Should().BeTrue();
        return (process.ExitCode, output, error);
    }

    private static string VerifyScript()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "scripts", "verify-package.sh");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "wickd-cli-boundary", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
