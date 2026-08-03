using AlgoForge.Data;
using Microsoft.EntityFrameworkCore;
var opt = new DbContextOptionsBuilder<AlgoForgeDbContext>()
    .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=AlgoForgeGeekedOn;Trusted_Connection=True;MultipleActiveResultSets=true")
    .Options;
using var db = new AlgoForgeDbContext(opt);
var photos       = await db.Photos.CountAsync();
var processed    = await db.Photos.CountAsync(p => p.FaceProcessingStatus == AlgoForge.Models.PhotoFaceProcessingStatus.Processed);
var failed       = await db.Photos.CountAsync(p => p.FaceProcessingStatus == AlgoForge.Models.PhotoFaceProcessingStatus.Failed);
var detections   = await db.PersonDetections.CountAsync();
var taggable     = await db.PersonDetections.CountAsync(d => d.IsTaggable);
var clusters     = await db.PersonClusters.CountAsync();
var unidentified = await db.PersonClusters.CountAsync(c => c.Status == AlgoForge.Models.PersonClusterStatus.Unidentified);
var identified   = await db.PersonClusters.CountAsync(c => c.Status == AlgoForge.Models.PersonClusterStatus.Identified);
var tags         = await db.Tags.CountAsync();
var suggested    = await db.Tags.CountAsync(t => t.Status == AlgoForge.Models.TagStatus.Suggested);
var confirmed    = await db.Tags.CountAsync(t => t.Status == AlgoForge.Models.TagStatus.Confirmed);
Console.WriteLine($"Photos: {photos} total | {processed} processed | {failed} failed");
Console.WriteLine($"PersonDetections: {detections} total | {taggable} taggable (Tier A)");
Console.WriteLine($"PersonClusters: {clusters} total | {unidentified} unidentified | {identified} identified");
Console.WriteLine($"Tags: {tags} total | {suggested} suggested | {confirmed} confirmed");
// Per-event breakdown
var events = await db.Events
    .Select(e => new {
        e.Name, e.Status,
        Photos = db.Photos.Count(p => p.EventId == e.Id),
        Processed = db.Photos.Count(p => p.EventId == e.Id && p.FaceProcessingStatus == AlgoForge.Models.PhotoFaceProcessingStatus.Processed),
        Failed = db.Photos.Count(p => p.EventId == e.Id && p.FaceProcessingStatus == AlgoForge.Models.PhotoFaceProcessingStatus.Failed),
        Detections = db.PersonDetections.Count(d => d.Photo!.EventId == e.Id),
        Clusters = db.PersonClusters.Count(c => c.EventId == e.Id),
        HasTaggable = db.PersonClusters.Count(c => c.EventId == e.Id && c.HasTaggableDetection)
    }).ToListAsync();
Console.WriteLine("\n--- Per Event ---");
foreach(var ev in events)
    Console.WriteLine($"  [{ev.Status}] {ev.Name}: {ev.Photos} photos ({ev.Processed} processed, {ev.Failed} failed), {ev.Detections} detections, {ev.Clusters} clusters ({ev.HasTaggable} surfaceable)");
