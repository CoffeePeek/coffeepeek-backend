using System.Reflection;
using System.Reflection.Emit;

namespace CoffeePeek.ShopsService.Tests.Architecture;

internal static class IlDependencyScanner
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => opcode.Value);

    public static IEnumerable<(MethodBase Caller, MethodBase Callee)> GetCalls(
        Assembly assembly, Func<Type, bool>? callerFilter = null)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            if (callerFilter is not null && !callerFilter(type))
                continue;

            foreach (var caller in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                var body = caller.GetMethodBody()?.GetILAsByteArray();
                if (body is null)
                    continue;

                var offset = 0;
                while (offset < body.Length)
                {
                    var value = (int)body[offset++];
                    if (value == 0xfe)
                        value = unchecked((short)(0xfe00 | body[offset++]));

                    var opcode = OpCodesByValue[(short)value];
                    var operandOffset = offset;
                    offset += OperandSize(opcode.OperandType, body, offset);

                    if (opcode.OperandType is not (OperandType.InlineMethod or OperandType.InlineTok))
                        continue;

                    var token = BitConverter.ToInt32(body, operandOffset);
                    MethodBase? callee;
                    try
                    {
                        callee = caller.Module.ResolveMember(
                            token,
                            caller.DeclaringType?.GetGenericArguments(),
                            caller.IsGenericMethod ? caller.GetGenericArguments() : null) as MethodBase;
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException($"Cannot resolve IL member in {caller} at {operandOffset}.", ex);
                    }

                    if (callee is not null)
                        yield return (caller, callee);
                }
            }
        }
    }

    private static int OperandSize(OperandType operandType, byte[] body, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(body, offset) * 4),
        _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}.")
    };
}
