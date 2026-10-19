using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK;

namespace Modules.NetHop
{
    /// <summary>Runs nethop.exe with --json and maps its exit codes to the module's action errors.</summary>
    internal static class NetHopCli
    {
        public const string NotInstalledError = "NetHopNotInstalled";
        public const string ServiceUnavailableError = "ServiceUnavailable";
        public const string AccessDeniedError = "AccessDenied";
        public const string FailedError = "Failed";

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        public static T Run<T>(params string[] arguments) where T : class
        {
            var (exitCode, stdout, stderr) = Execute(arguments.Concat(new[] { "--json" }).ToArray());
            switch (exitCode)
            {
                case 0:
                case 1 when stdout.TrimStart().StartsWith("{", StringComparison.Ordinal) || stdout.TrimStart().StartsWith("[", StringComparison.Ordinal):
                    return Deserialize<T>(stdout);
                case 3:
                    throw new ActionException(ServiceUnavailableError, "The NetHop service is not running.");
                case 4:
                    throw new ActionException(AccessDeniedError, "NetHop requires an administrator account.");
                default:
                    throw new ActionException(FailedError, Message(stderr, stdout, exitCode));
            }
        }

        public static void RunPlain(params string[] arguments)
        {
            var (exitCode, stdout, stderr) = Execute(arguments);
            if (exitCode == 3)
                throw new ActionException(ServiceUnavailableError, "The NetHop service is not running.");
            if (exitCode == 4)
                throw new ActionException(AccessDeniedError, "NetHop requires an administrator account.");
            if (exitCode != 0)
                throw new ActionException(FailedError, Message(stderr, stdout, exitCode));
        }

        private static (int ExitCode, string Stdout, string Stderr) Execute(string[] arguments)
        {
            var start = new ProcessStartInfo(FindCli(), string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var process = Process.Start(start))
            {
                // Read both streams asynchronously so a full stderr buffer cannot block the child.
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    process.Kill();
                    throw new ActionException(FailedError, "nethop.exe did not finish in time.");
                }
                return (process.ExitCode, stdout, stderrTask.Result);
            }
        }

        private static string FindCli()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("NETHOP_CLI");
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
                return fromEnvironment;

            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NetHop", "nethop.exe");
            if (File.Exists(installed))
                return installed;

            var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Where(dir => dir.Length > 0)
                .Select(dir => Path.Combine(dir.Trim(), "nethop.exe"))
                .FirstOrDefault(File.Exists);
            if (onPath != null)
                return onPath;

            throw new ActionException(NotInstalledError, "nethop.exe was not found. Install NetHop on this machine.");
        }

        private static T Deserialize<T>(string json) where T : class
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)serializer.ReadObject(stream);
        }

        private static string Message(string stderr, string stdout, int exitCode)
        {
            var text = (stderr + " " + stdout).Trim();
            return text.Length > 0 ? text : $"nethop.exe exited with code {exitCode}.";
        }

        private static string Quote(string argument) =>
            argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0
                ? argument
                : "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // JSON shapes printed by nethop.exe --json (camelCase).

    [DataContract]
    internal sealed class ProfileSummaryJson
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
        [DataMember(Name = "isActive")] public bool IsActive { get; set; }
    }

    [DataContract]
    internal sealed class StatusJson
    {
        [DataMember(Name = "activeProfileName")] public string ActiveProfileName { get; set; }
        [DataMember(Name = "activeProfileDrifted")] public bool ActiveProfileDrifted { get; set; }
        [DataMember(Name = "automationsPausedUntil")] public string AutomationsPausedUntil { get; set; }
        [DataMember(Name = "ssidUnavailable")] public bool SsidUnavailable { get; set; }
    }

    [DataContract]
    internal sealed class IssueJson
    {
        [DataMember(Name = "severity")] public string Severity { get; set; }
        [DataMember(Name = "path")] public string Path { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
    }

    [DataContract]
    internal sealed class ApplyResultJson
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "dryRun")] public bool DryRun { get; set; }
        [DataMember(Name = "profileName")] public string ProfileName { get; set; }
        [DataMember(Name = "steps")] public string[] Steps { get; set; }
        [DataMember(Name = "issues")] public IssueJson[] Issues { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "rolledBack")] public bool RolledBack { get; set; }
    }
}
