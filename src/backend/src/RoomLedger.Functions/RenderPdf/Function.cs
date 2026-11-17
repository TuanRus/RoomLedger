using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.RenderPdf;

/**
 * Lambda render-pdf (MD-06-FE-05, BR-29): TenancySettled → biên bản thanh lý PDF trong bucket derived.
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private readonly Func<AppDbContext> _db;
    private readonly IObjectStore _store;

    public Function() : this(FunctionDb.Create, FunctionAws.ObjectStore)
    {
    }

    public Function(Func<AppDbContext> db, IObjectStore store)
    {
        _db = db;
        _store = store;
    }

    public Task HandleAsync(CloudWatchEvent<TenancySettledDetail> input, ILambdaContext context) => throw new NotImplementedException();
}
