using System.Buffers.Binary;
using BashkirSchool.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BashkirSchool.Tests;

public sealed class AudioAdapterTests
{
    [Fact]
    public async Task MinioLinkIsSignedForPrivateBucketWithoutConnectingToStorage()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AudioStorage:Endpoint"] = "https://minio.example.test", ["AudioStorage:Bucket"] = "school-audio",
            ["AudioStorage:AccessKey"] = "test-access", ["AudioStorage:SecretKey"] = "test-secret", ["AudioStorage:LinkMinutes"] = "15"
        }).Build();
        using var storage = new S3AudioStorage(configuration, TimeProvider.System);
        var url = new Uri(await storage.ListenUrlAsync("speech/v5_cis_base/test.wav", default));
        Assert.Equal("minio.example.test", url.Host);
        Assert.Equal("/school-audio/speech/v5_cis_base/test.wav", url.AbsolutePath);
        Assert.Contains("X-Amz-Signature=", url.Query); Assert.Contains("X-Amz-Expires=900", url.Query);
        Assert.DoesNotContain("test-secret", url.AbsoluteUri);
    }

    [SileroFact]
    public async Task RealSileroSynthesizesBashkirWavAndReusesTheCachedModel()
    {
        var model = Environment.GetEnvironmentVariable("BASHKIR_TEST_SILERO_MODEL")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Speech:PythonExecutable"] = Environment.GetEnvironmentVariable("BASHKIR_TEST_SILERO_PYTHON"),
            ["Speech:ModelPath"] = model, ["Speech:TimeoutSeconds"] = "600"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddHttpClient();
        using var provider = services.BuildServiceProvider();
        using var speech = new SileroSpeechSynthesizer(configuration, provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SileroSpeechSynthesizer>>());
        var first = await speech.SynthesizeAsync("Һаумы, донъя!", "ba", default);
        Assert.Equal("audio/wav", first.ContentType); Assert.True(first.Data.Length > 4800);
        Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(first.Data.AsSpan(24, 4)));
        Assert.Contains(first.Data.Skip(44), sample => sample != 0);
        var cachedAt = File.GetLastWriteTimeUtc(model);
        var second = await speech.SynthesizeAsync("Мин мәктәпкә барам.", "ba", default);
        Assert.True(second.Data.Length > 4800); Assert.Equal(cachedAt, File.GetLastWriteTimeUtc(model));
        Assert.False(first.Data.SequenceEqual(second.Data));
    }
}

public sealed class SileroFactAttribute : FactAttribute
{
    public SileroFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BASHKIR_TEST_SILERO_PYTHON")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BASHKIR_TEST_SILERO_MODEL")))
            Skip = "Set BASHKIR_TEST_SILERO_PYTHON and BASHKIR_TEST_SILERO_MODEL for real CPU inference.";
    }
}
