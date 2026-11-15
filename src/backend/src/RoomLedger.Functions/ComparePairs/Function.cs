using Amazon.Lambda.Core;
using RoomLedger.Ai.Compare;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;

namespace RoomLedger.Functions.ComparePairs;

/** RED stub. */
public class Function(IObjectStore store, RoomPhotoComparer comparer)
{
    public Task<PairResult> HandleAsync(CompareTask input, ILambdaContext context) => throw new NotImplementedException();
}
