// Serialises the test classes in this assembly. Every one of them talks to a real PostgreSQL cluster,
// and one of them sweeps the catalog for databases to DROP — so they are kept from overlapping rather
// than each being made to tolerate the others mid-sweep.
//
// This has to be an assembly attribute: <xUnitDisableParallelization> in the csproj is an MSBuild
// property xunit never reads, so setting it there looks like this and does nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
