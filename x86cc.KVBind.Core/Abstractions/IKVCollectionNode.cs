using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Abstractions;

public interface IKVCollectionNode
{
    KVNodeBase? Parent { get; }

    void Bind(KVModel model, KVCollectionDefinition definition, KVNodeBase? parent = null);

    KVNode? GetById(string itemId);

    KVNode Create(Guid itemId, string? typeToken = null);

    bool RemoveById(string itemId);

    bool MoveById(string itemId, int toIndex);

    IReadOnlyList<string> GetActiveItemIds();
}
