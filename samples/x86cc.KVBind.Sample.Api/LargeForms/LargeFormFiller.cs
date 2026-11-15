using x86cc.KVBind.Core;
using x86cc.KVBind.Schema;

namespace x86cc.KVBind.Sample.Api.LargeForms;

/// <summary>
/// Builds patches that fill a large form with valid values for every field, read from its schema — what a
/// client filling the whole form would send. Used by the integration tests, the benchmarks and the load run.
/// </summary>
public static class LargeFormFiller
{
    public static IEnumerable<KVPatchOperation> FillAll(KVSchemaDocument schema, int variant = 0)
    {
        foreach (var group in schema.Root.Groups)
        {
            foreach (var field in group.Fields)
            {
                yield return KVPatchOperation.Set($"/{group.Key}/{field.Key}", ValueFor(field, variant));
            }
        }
    }

    /// <summary>Lines 1..count with Amount = i × 10, so the derived total is predictable.</summary>
    public static IEnumerable<KVPatchOperation> AddLines(int count, string prefix = "L")
    {
        for (var i = 1; i <= count; i++)
        {
            var id = $"{prefix}{i}";
            yield return KVPatchOperation.Add("/Lines", new KVAddPatchPayload(id));
            yield return KVPatchOperation.Set($"/Lines/{id}/Description", $"Line {i}");
            yield return KVPatchOperation.Set($"/Lines/{id}/UnitPrice", 10m);
            yield return KVPatchOperation.Set($"/Lines/{id}/Amount", i * 10m);
            yield return KVPatchOperation.Set($"/Lines/{id}/Category", "goods");
            yield return KVPatchOperation.Set($"/Lines/{id}/Reference", "ABC-123");
        }
    }

    public static object ValueFor(KVFieldSchema field, int variant = 0)
    {
        if (field.AllowedValues is { } allowed)
        {
            var option = allowed.Options[variant % allowed.Options.Count].Id;
            return allowed.AppliesToElements ? new[] { option } : option;
        }

        return field.Kind switch
        {
            KVSchemaValueKind.Integer => 10 + variant,
            KVSchemaValueKind.Decimal => 12.5m + variant,
            KVSchemaValueKind.Boolean => variant % 2 == 0,
            KVSchemaValueKind.Date => new DateOnly(2027, 1, 1).AddDays(variant),
            _ when field.Pattern is not null => "ABC-123",
            _ => $"Value {variant} of {field.Key}",
        };
    }
}
