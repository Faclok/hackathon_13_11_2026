using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BashkirSchool.Application;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

public sealed class S3AudioStorage(IConfiguration configuration, TimeProvider clock) : IAudioStorage, IDisposable
{
    // No credentials, network or SDK initialization until audio is actually needed.
    private AmazonS3Client? client;
    private AmazonS3Client Client => client ??= new(new BasicAWSCredentials(Required("AccessKey"), Required("SecretKey")), new AmazonS3Config
    {
        ServiceURL = Required("Endpoint"), AuthenticationRegion = configuration["AudioStorage:Region"] ?? "us-east-1", ForcePathStyle = true
    });
    private string Required(string name) => configuration["AudioStorage:" + name] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"AudioStorage:{name} is required for audio exercises.");
    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try { await Client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = Required("Bucket"), Key = key }, ct); return true; }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
    }
    public async Task UploadAsync(string key, SynthesizedSpeech speech, CancellationToken ct)
    {
        using var stream = new MemoryStream(speech.Data, writable: false);
        await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Required("Bucket"), Key = key, ContentType = speech.ContentType, InputStream = stream,
            AutoCloseStream = false
        }, ct);
    }
    public Task<string> ListenUrlAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var minutes = configuration.GetValue("AudioStorage:LinkMinutes", 30);
        if (minutes is < 1 or > 1440) throw new InvalidOperationException("AudioStorage:LinkMinutes must be 1..1440.");
        // Endpoint must be browser-accessible: replacing the host after signing invalidates SigV4.
        return Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = Required("Bucket"), Key = key, Verb = HttpVerb.GET, Expires = clock.GetUtcNow().AddMinutes(minutes).UtcDateTime
        });
    }
    public void Dispose() => client?.Dispose();
}
