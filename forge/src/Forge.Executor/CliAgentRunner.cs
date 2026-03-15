using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Executor;

public partial class CliAgentRunner : IAgentRunner
{
    private readonly string _command;
    private readonly int _timeoutSeconds;
    private readonly string? _defaultWorkDir;
    private readonly ILogger<CliAgentRunner> _logger;

    public CliAgentRunner(IOptions<ForgeOptions> options, ILogger<CliAgentRunner> logger)
    {
        var cli = options.Value.Executor.Cli;
        _command = cli.Command;
        _timeoutSeconds = cli.TimeoutSeconds;
        _defaultWorkDir = cli.WorkDir;
        _logger = logger;
    }

    public async Task<AgentResult> RunAsync(string prompt, string workDir, CancellationToken ct = default)
    {
        var effectiveDir = workDir ?? _defaultWorkDir
            ?? throw new InvalidOperationException("No work_dir provided and no default configured");

        var psi = new ProcessStartInfo
        {
            FileName = _command,
            ArgumentList = { "--print", "--output-format", "json", "-p", prompt },
            WorkingDirectory = effectiveDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Executing claude CLI in {WorkDir} (timeout={Timeout}s, prompt length={Length})",
            effectiveDir, _timeoutSeconds, prompt.Length);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        Process process;
        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("Failed to start process");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _logger.LogError("Claude CLI command not found: {Command}", _command);
            return new AgentResult(false, ErrorMessage: $"CLI command not found: {_command}");
        }

        using (process)
        {
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

                await process.WaitForExitAsync(timeoutCts.Token);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                var tokenUsage = ExtractTokenUsage(stdout);
                var changedFiles = ExtractChangedFiles(stdout);

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Claude CLI exited with code {ExitCode}: {Stderr}",
                        process.ExitCode, stderr[..Math.Min(stderr.Length, 500)]);

                    return new AgentResult(
                        Success: false,
                        Output: stdout,
                        ErrorMessage: $"Exit code {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 1000)]}",
                        TokenUsage: tokenUsage,
                        ChangedFiles: changedFiles);
                }

                _logger.LogInformation("Claude CLI completed successfully");
                return new AgentResult(
                    Success: true,
                    Output: stdout,
                    TokenUsage: tokenUsage,
                    ChangedFiles: changedFiles);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogError("Claude CLI timed out after {Timeout}s", _timeoutSeconds);
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new AgentResult(false, ErrorMessage: $"CLI timed out after {_timeoutSeconds}s");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    internal static Dictionary<string, object>? ExtractTokenUsage(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
                return JsonElementToDict(usage);

            if (root.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("usage", out var resultUsage))
                return JsonElementToDict(resultUsage);
        }
        catch (JsonException) { }

        // Fallback: regex
        var dict = new Dictionary<string, object>();
        var inputMatch = InputTokensRegex().Match(output);
        if (inputMatch.Success)
            dict["input_tokens"] = int.Parse(inputMatch.Groups[1].Value);

        var outputMatch = OutputTokensRegex().Match(output);
        if (outputMatch.Success)
            dict["output_tokens"] = int.Parse(outputMatch.Groups[1].Value);

        return dict.Count > 0 ? dict : null;
    }

    internal static List<string> ExtractChangedFiles(string output)
    {
        var files = new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray())
                {
                    if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            if (block.TryGetProperty("type", out var type) &&
                                type.GetString() == "tool_use" &&
                                block.TryGetProperty("input", out var input))
                            {
                                string? path = null;
                                if (input.TryGetProperty("file_path", out var fp))
                                    path = fp.GetString();
                                else if (input.TryGetProperty("path", out var p))
                                    path = p.GetString();

                                if (path is not null)
                                    files.Add(path);
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException) { }

        return files.Order().ToList();
    }

    private static Dictionary<string, object> JsonElementToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => prop.Value.GetRawText()
            };
        }
        return dict;
    }

    [GeneratedRegex(@"input[_ ]tokens?:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex InputTokensRegex();

    [GeneratedRegex(@"output[_ ]tokens?:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OutputTokensRegex();
}
