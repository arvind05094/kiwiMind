using Amazon.S3;
using Amazon.S3.Model;
using KiwiMind.Application.Common.Interfaces;
using KiwiMind.Application.Common.Settings;
using Microsoft.Extensions.Options;

namespace KiwiMind.Infrastructure.Storage;

public class S3BlobStorageService : IBlobStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public S3BlobStorageService(IOptions<S3StorageSettings> settings)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = settings.Value.ServiceUrl,
            ForcePathStyle = true
        };
        _s3Client = new AmazonS3Client(
            settings.Value.AccessKey,
            settings.Value.SecretKey,
            config);
        _bucketName = settings.Value.BucketName;
    }

    public async Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = blobName,
            InputStream = content,
            ContentType = contentType
        };
        await _s3Client.PutObjectAsync(request, cancellationToken);
        return $"{_bucketName}/{blobName}";
    }

    public async Task DeleteAsync(string blobUri, CancellationToken cancellationToken)
    {
        var key = ExtractKey(blobUri);
        await _s3Client.DeleteObjectAsync(_bucketName, key, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string blobUri, CancellationToken cancellationToken)
    {
        var key = ExtractKey(blobUri);
        var response = await _s3Client.GetObjectAsync(_bucketName, key, cancellationToken);
        return response.ResponseStream;
    }

    private static string ExtractKey(string blobUri)
    {
        // Handle both "bucket/key" format and full URIs
        if (Uri.TryCreate(blobUri, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http"))
            return uri.AbsolutePath.TrimStart('/').Split('/', 2).Last();
        // Simple "bucket/key" format
        var slashIndex = blobUri.IndexOf('/');
        return slashIndex >= 0 ? blobUri[(slashIndex + 1)..] : blobUri;
    }
}
