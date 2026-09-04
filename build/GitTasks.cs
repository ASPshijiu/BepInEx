using Cake.Common;
using Cake.Core;

static class GitTasks
{
    public static string Git(this ICakeContext ctx, string args, string separator = "")
    {
        using var process = ctx.StartAndReturnProcess("git", new()
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        process.WaitForExit();
        var exitCode = process.GetExitCode();
        if (exitCode != 0)
        {
            var error = string.Join("\n", process.GetStandardError());
            throw new CakeException($"git {args} failed with exit code {exitCode}: {error}");
        }

        return string.Join(separator, process.GetStandardOutput());
    }
}
