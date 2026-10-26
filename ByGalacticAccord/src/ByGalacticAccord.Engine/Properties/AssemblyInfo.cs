using System.Runtime.CompilerServices;

// Stock, storage and contract state change only through SimulationContext and Market, so the goods and money
// ledgers stay whole; the tests reach the same internals to set up a world.
[assembly: InternalsVisibleTo("ByGalacticAccord.Tests")]
