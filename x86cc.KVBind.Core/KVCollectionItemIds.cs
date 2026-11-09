using System;

namespace x86cc.KVBind.Core;

// Collection item ids become canonical path segments (e.g. Animals/Snake): letters, digits, '_' and '-',
// so GUIDs in "D" format stay valid. Anything else ('/', whitespace, reserved '$' keys such as $items and
// $type) is rejected. Ids are case-sensitive.
public static class KVCollectionItemIds
{
    public static bool IsValid(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        foreach (var c in itemId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) return false;
        }

        return true;
    }

    internal static void EnsureValid(string? itemId)
    {
        if (!IsValid(itemId))
        {
            throw new InvalidOperationException($"Collection item id '{itemId}' is invalid; ids may only contain A-Z, a-z, 0-9, '_' and '-'.");
        }
    }

    internal static string FromGuid(Guid itemId)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("Collection item id cannot be empty.", nameof(itemId));
        return itemId.ToString("D");
    }
}
