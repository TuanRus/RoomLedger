using Xunit;

// CDK runs on a single jsii (Node) runtime per process; synthesizing from several
// test classes in parallel makes it fail or hang. Run test classes one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
