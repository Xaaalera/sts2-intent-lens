using System.Reflection;

namespace IntentLens;

public static class ConstantWeight
{
    public static double? Read(Delegate function)
    {
        var bytes = function.Method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null) { return null; }
        var start = 0;
        while (start < bytes.Length && bytes[start] == 0) { start++; }
        var code = bytes.AsSpan(start);
        if (code.Length == 6 && code[0] == 0x22 && code[5] == 0x2a)
        { return BitConverter.ToSingle(code.Slice(1, 4)); }
        if (code.Length == 7 && code[0] == 0x02 && code[1] == 0x7b && code[6] == 0x2a && function.Target is not null)
        {
            var field = function.Method.Module.ResolveField(BitConverter.ToInt32(code.Slice(2, 4)));
            if (field?.FieldType == typeof(float) && !field.IsStatic && field.DeclaringType!.IsInstanceOfType(function.Target))
            { return (float)field.GetValue(function.Target)!; }
        }
        return null;
    }
}
