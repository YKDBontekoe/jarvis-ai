using Amazon.S3;
using Amazon.S3.Model;
using Jarvis.Application.Files;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Infrastructure.Files;

public sealed class S3ObjectStorage(IAmazonS3 client, IConfiguration configuration) : IObjectStorage
{
    private readonly string _bucket = configuration["ObjectStorage:Bucket"]
        ?? throw new InvalidOperationException("ObjectStorage:Bucket is required.");

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            // Send a normally signed, fixed-length payload to the self-hosted S3 endpoint.
            UseChunkEncoding = false,
            DisablePayloadSigning = false,
            AutoCloseStream = false,
            AutoResetStreamPosition = false
        }, cancellationToken);
    }

    public async Task<Stream?> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectAsync(_bucket, objectKey, cancellationToken);
            return new OwnedResponseStream(response);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(_bucket, objectKey, cancellationToken);

    private sealed class OwnedResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing) response.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            response.Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
