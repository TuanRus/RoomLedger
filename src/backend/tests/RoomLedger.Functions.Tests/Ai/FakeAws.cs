using System.Reflection;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Giả một interface của AWS SDK (rất nhiều method) bằng DispatchProxy: chỉ method được test xử lý, method khác ném lỗi.
 * Handler nhận tên method và tham số, trả kết quả; hàm async tự bọc vào Task.
 *
 * @author Tuấn
 * @version 1.0
 */
public class FakeAws<T> : DispatchProxy where T : class
{
    private Func<string, object?[], object?> _handler = (name, _) => throw new NotSupportedException(name);

    public List<(string Method, object? Request)> Calls { get; } = [];

    public static T Create(Func<string, object?[], object?> handler, out FakeAws<T> fake)
    {
        var proxy = Create<T, FakeAws<T>>();
        fake = (FakeAws<T>)(object)proxy;
        fake._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        Calls.Add((name, args?.FirstOrDefault()));
        var result = _handler(name, args ?? []);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }

        return result;
    }
}
