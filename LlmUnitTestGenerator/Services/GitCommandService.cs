using System.Diagnostics;
using System.Text;

namespace LlmUnitTestGenerator.Services;

public sealed class GitCommandService
{
    public async Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var message = new StringBuilder()
                .AppendLine($"git {string.Join(" ", args)} failed with code {process.ExitCode}.")
                .AppendLine(stderr)
                .ToString();
            throw new InvalidOperationException(message);
        }

        return stdout;
    }
}
