using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BashkirSchool.Infrastructure;

/// <summary>C# owns a lazy, persistent local inference process. Silero's torch.package needs its Python wrapper.</summary>
public sealed class SileroSpeechSynthesizer(IConfiguration configuration, IHttpClientFactory clients,
    ILogger<SileroSpeechSynthesizer> logger) : ISpeechSynthesizer, IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private Process? worker;

    public async Task<SynthesizedSpeech> SynthesizeAsync(string text, string language, CancellationToken ct)
    {
        Guard.Text(text, "text", 2000);
        if (language is not ("ba" or "ru") && string.IsNullOrEmpty(configuration[$"Speech:Speakers:{language}"]))
            throw new DomainException("No Silero voice configured for the source language.");
        await gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("Speech:TimeoutSeconds", 180)));
            var token = timeout.Token;
            if (worker is null || worker.HasExited) await Start(token);
            var speaker = configuration[$"Speech:Speakers:{language}"] ?? (language == "ba" ? "bak_aigul" : "ru_bak_aigul");
            var sampleRate = configuration.GetValue("Speech:SampleRate", 24000);
            if (sampleRate is not (8000 or 24000 or 48000)) throw new InvalidOperationException("Speech:SampleRate must be 8000, 24000 or 48000.");
            await worker!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { text, speaker, sampleRate }).AsMemory(), token);
            await worker.StandardInput.FlushAsync(token);
            var line = await worker.StandardOutput.ReadLineAsync(token) ?? throw new InvalidOperationException("Silero inference process exited.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException("Silero inference failed: " + error.GetString());
            var bytes = response.RootElement.GetProperty("wav").GetBytesFromBase64();
            if (bytes.Length < 44 || bytes.Length > 32 * 1024 * 1024 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8))
                throw new InvalidOperationException("Silero returned an invalid WAV file.");
            return new(bytes, "audio/wav", ".wav");
        }
        catch { Stop(); throw; }
        finally { gate.Release(); }
    }

    private async Task Start(CancellationToken ct)
    {
        Stop();
        var path = Path.GetFullPath(configuration["Speech:ModelPath"] ?? Path.Combine(AppContext.BaseDirectory, "models", "v5_cis_base.pt"));
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using var client = clients.CreateClient("silero-model");
                using var response = await client.GetAsync("https://models.silero.ai/models/tts/ru/v5_cis_base.pt", HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using (var file = File.Create(temporary)) await response.Content.CopyToAsync(file, ct);
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var start = new ProcessStartInfo(configuration["Speech:PythonExecutable"] ?? (OperatingSystem.IsWindows() ? "python" : "python3"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Speech", "silero_worker.py"));
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(configuration["Speech:Threads"] ?? "2");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start Silero inference.");
        worker.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) logger.LogDebug("Silero: {Message}", e.Data); };
        worker.BeginErrorReadLine();
    }
    private void Stop()
    {
        if (worker is null) return;
        try { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
        finally { worker.Dispose(); worker = null; }
    }
    public void Dispose() { Stop(); gate.Dispose(); }
}
