using System.Reflection;

public class Service<T> : DispatchProxy where T : class
{
    public readonly Dictionary<string, object?> Values = [];
    public readonly Dictionary<string, Func<object?[]?, object?>> Methods = [];
    public static T Create() => Create<T, Service<T>>();
    public static Service<T> For(T service) => (Service<T>)(object)service;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        var name = method!.Name;
        if (name.StartsWith("get_")) return Values.GetValueOrDefault(name[4..]);
        if (name.StartsWith("set_")) { Values[name[4..]] = arguments![0]; return null; }
        if (Methods.TryGetValue(name, out var invoke)) return invoke(arguments);
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
