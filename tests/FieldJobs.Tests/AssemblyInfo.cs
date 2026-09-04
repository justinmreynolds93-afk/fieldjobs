using Xunit;

// FieldJobs.Models.Vocab and FieldJobs.Config.AppConfig.Instance are process-wide
// static state (mirroring how the real app configures them once at startup) —
// tests that call Vocab.Configure()/AppConfig.Load() must not run concurrently
// with each other or they'll race on that shared state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
