using Serilog.Core;
using Serilog.Events;

namespace CoffeePeek.Shared.Web.Logging;

/// <summary>
/// Walks structured log properties and redacts secrets by value and property name.
/// YARP logs the destination URL including SignalR's access_token query string.
/// </summary>
public sealed class SensitiveDataRedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var key in logEvent.Properties.Keys.ToArray())
        {
            var original = logEvent.Properties[key];
            var redacted = Redact(original, key);
            if (!ReferenceEquals(original, redacted))
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, redacted));
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value, string? propertyName = null) =>
        IsSensitivePropertyName(propertyName)
            ? new ScalarValue("[REDACTED]")
            : RedactValue(value);

    private static LogEventPropertyValue RedactValue(LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue { Value: string text } =>
                RedactScalar(text, value),
            SequenceValue sequence =>
                RedactSequence(sequence),
            StructureValue structure =>
                RedactStructure(structure),
            DictionaryValue dictionary =>
                RedactDictionary(dictionary),
            _ => value
        };

    private static bool IsSensitivePropertyName(string? name)
    {
        if (name is null)
            return false;

        var normalized = name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);

        return normalized.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("token", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("secret", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("apikey", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("nonce", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("setcookie", StringComparison.OrdinalIgnoreCase);
    }

    private static LogEventPropertyValue RedactScalar(string text, LogEventPropertyValue original)
    {
        var redacted = SensitiveDataRedactor.Redact(text);
        return redacted == text ? original : new ScalarValue(redacted);
    }

    private static LogEventPropertyValue RedactSequence(SequenceValue sequence)
    {
        var changed = false;
        var elements = new LogEventPropertyValue[sequence.Elements.Count];
        for (var i = 0; i < sequence.Elements.Count; i++)
        {
            elements[i] = Redact(sequence.Elements[i]);
            changed |= !ReferenceEquals(elements[i], sequence.Elements[i]);
        }

        return changed ? new SequenceValue(elements) : sequence;
    }

    private static LogEventPropertyValue RedactStructure(StructureValue structure)
    {
        var changed = false;
        var props = new LogEventProperty[structure.Properties.Count];
        for (var i = 0; i < structure.Properties.Count; i++)
        {
            var property = structure.Properties[i];
            var redacted = Redact(property.Value, property.Name);
            changed |= !ReferenceEquals(redacted, property.Value);
            props[i] = ReferenceEquals(redacted, property.Value)
                ? property
                : new LogEventProperty(property.Name, redacted);
        }

        return changed ? new StructureValue(props, structure.TypeTag) : structure;
    }

    private static LogEventPropertyValue RedactDictionary(DictionaryValue dictionary)
    {
        var changed = false;
        var pairs = new KeyValuePair<ScalarValue, LogEventPropertyValue>[dictionary.Elements.Count];
        var i = 0;
        foreach (var (key, element) in dictionary.Elements)
        {
            var redactedKey = key;
            if (key.Value is string keyText)
            {
                var redactedKeyText = SensitiveDataRedactor.Redact(keyText);
                if (redactedKeyText != keyText)
                {
                    redactedKey = new ScalarValue(redactedKeyText);
                    changed = true;
                }
            }

            var redactedValue = Redact(element, key.Value as string);
            if (!ReferenceEquals(redactedValue, element))
                changed = true;

            pairs[i++] = new KeyValuePair<ScalarValue, LogEventPropertyValue>(redactedKey, redactedValue);
        }

        return changed ? new DictionaryValue(pairs) : dictionary;
    }
}
