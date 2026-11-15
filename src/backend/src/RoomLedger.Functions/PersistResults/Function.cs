using Amazon.Lambda.Core;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PersistResults;

/** RED stub. */
public class Function(Func<AppDbContext> db)
{
    public Task<PersistOutput> HandleAsync(PersistInput input, ILambdaContext context) => throw new NotImplementedException();
}
