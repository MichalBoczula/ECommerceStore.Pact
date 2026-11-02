using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;
using Microsoft.Extensions.Configuration;

namespace ECommerceStore.Pact.Infrastructure.History;

public sealed class OasDiffSchemaComparer(IConfiguration configuration) : IContractSchemaComparer, IDisposable
{
    public const string Version = "1.35.0";
    private readonly SemaphoreSlim _gate = new(2, 2);
    private string Executable => configuration["Comparison:Executable"] ?? Environment.GetEnvironmentVariable("OASDIFF_PATH") ?? "oasdiff";

    public async Task<SchemaAnalysis> CompareAsync(ContractArtifact before, ContractArtifact after, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var entered = false;
        var directory = Path.Combine(Path.GetTempPath(), "pact-diff-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _gate.WaitAsync(timeout.Token); entered = true;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "config.json"), "{}", timeout.Token);
            await File.WriteAllBytesAsync(Path.Combine(directory, "before.json"), WithoutBom(before.GetContent()), timeout.Token);
            await File.WriteAllBytesAsync(Path.Combine(directory, "after.json"), WithoutBom(after.GetContent()), timeout.Token);
            var version = await RunAsync(directory, ["--version"], 1024, timeout.Token);
            if (version.Trim() != "oasdiff version " + Version) throw new InvalidOperationException("Unexpected comparison tool version.");
            var output = await RunAsync(directory, ["changelog", "before.json", "after.json", "--format", "json", "--lang", "en",
                "--allow-external-refs=false", "--flatten-allof", "--flatten-params", "--config", "config.json"], 4 * 1024 * 1024, timeout.Token);
            using var parsed = JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "[]" : output);
            if (parsed.RootElement.ValueKind != JsonValueKind.Array || parsed.RootElement.GetArrayLength() > 1000)
                throw new InvalidOperationException("Unexpected or excessive comparison output.");
            var findings = parsed.RootElement.EnumerateArray().Select(item => new SchemaFinding(item.GetProperty("id").GetString()!,
                item.GetProperty("level").GetInt32(), item.GetProperty("text").GetString()!,
                item.TryGetProperty("operation", out var method) ? method.GetString() : null,
                item.TryGetProperty("path", out var path) ? path.GetString() : null)).ToArray();
            var partial = HasUnassessedSemantics(before) || HasUnassessedSemantics(after);
            var limitations = partial ? new[] { "Conservative partial assessment: deprecation/time policies, callbacks/webhooks or advanced JSON Schema keywords occur. Inspect the explicit artifacts." } : [];
            var assessment = findings.Any(f => f.Level >= 2) ? SchemaAssessment.PotentiallyBreaking : partial ? SchemaAssessment.NotAssessed : SchemaAssessment.NoBreakingChangesDetected;
            return new SchemaAnalysis("oasdiff", Version, assessment, findings, limitations);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        { throw new ComparisonUnavailableException(exception); }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            finally { if (entered) _gate.Release(); }
        }
    }

    private async Task<string> RunAsync(string directory, IEnumerable<string> arguments, int maxOutputBytes, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Executable) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("OASDIFF_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
        start.Environment["LANG"] = "C"; start.Environment["LC_ALL"] = "C"; start.Environment["TZ"] = "UTC";
        using var process = Process.Start(start) ?? throw new Win32Exception("Comparison process could not start.");
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, maxOutputBytes, cancellationToken);
            var error = ReadBoundedAsync(process.StandardError, 32768, cancellationToken);
            await await Task.WhenAny(output, error);
            await Task.WhenAll(output, error);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException("Comparison process failed.");
            return await output;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxBytes, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; var bytes = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            bytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (bytes > maxBytes) throw new InvalidOperationException("Comparison output exceeded the limit.");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static byte[] WithoutBom(byte[] bytes) => bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? bytes[3..] : bytes;
    private static bool HasUnassessedSemantics(ContractArtifact artifact)
    {
        using var document = JsonDocument.Parse(WithoutBom(artifact.GetContent()));
        return Partial(document.RootElement);
    }
    private static bool Partial(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(p =>
            p.Name is "callbacks" or "webhooks" or "$dynamicRef" or "$dynamicAnchor" or "$id" or "$schema" or "unevaluatedProperties" or "unevaluatedItems" or
                "dependentSchemas" or "if" or "then" or "else" or "contains" or "minContains" or "maxContains" or "x-sunset" ||
            (p.Name == "deprecated" && p.Value.ValueKind == JsonValueKind.True) || Partial(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(Partial),
        _ => false
    };
    public void Dispose() => _gate.Dispose();
}
