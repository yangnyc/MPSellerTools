// Integration tests boot a real TenantHost via MPST_INSTANCE_CONFIG_FILE, a
// process-wide environment variable (see Integration/TenantHostFixture.cs) —
// running two fixtures' startup concurrently would race on it. Real xUnit
// unit tests (Unit/*) have no such constraint but sharing the assembly-level
// setting keeps this simple and avoids ever debugging a rare, hard-to-repro
// parallel-only failure to save a few seconds of wall-clock test time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
