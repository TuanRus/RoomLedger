using Amazon.Lambda.Core;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PlanCompare;

/** RED stub. */
public class Function(Func<AppDbContext> db, IObjectStore store)
{
    public Task<ComparePlan> HandleAsync(CompareStart input, ILambdaContext context) => throw new NotImplementedException();
}
