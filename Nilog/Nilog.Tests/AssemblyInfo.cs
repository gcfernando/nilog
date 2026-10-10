// Serializes the suite: tests mutate process-wide statics (template cache limits, flush
// callbacks, formatter hooks), so cross-class parallelism would make them order-dependent.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
