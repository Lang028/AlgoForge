using System.Threading.Channels;

namespace AlgoForge.Services.PersonPipeline
{
    // In-process work queue for face detection -- the fix for the seam PersonPipelineService
    // already flags: "moving them behind a queue is a change to the call sites in
    // PhotosController, not to any logic here." A photo lands here the moment its row and
    // file are saved; PhotoDetectionWorker drains it on its own schedule, so the HTTP
    // request that uploaded or retried it returns immediately instead of waiting 1-3s per
    // photo -- which is also what let the client-side batching (8 files a request) exist in
    // the first place. Singleton and unbounded: this is one process on one machine (D19),
    // not a distributed queue, and a bounded channel would just make Enqueue block.
    public class PhotoDetectionQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

        public void Enqueue(Guid photoId) => _channel.Writer.TryWrite(photoId);

        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
