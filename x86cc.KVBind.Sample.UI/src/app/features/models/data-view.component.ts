import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { DataObject, ID_KEY, KVItemTypeSchema, KVNestedNodeSchema, KVNodeSchema, KVCollectionSchema, TYPE_KEY, labelOf, optionsOf, titleField, typeLabel, KVFieldSchema } from './schema';

// Read-only rendering of projected data, walked recursively against the schema: fields as label/value
// pairs, groups as sections, collections as item lists, nested nodes by their active subtype.
@Component({
  selector: 'app-data-view',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (node.fields.length > 0) {
      <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
        @for (field of node.fields; track field.key) {
          <div class="min-w-0">
            <dt class="text-xs uppercase tracking-wide text-gray-500">{{ labelOf(field) }}
              @if (isInherited(field.key)) { <span class="ml-1 rounded bg-sky-100 px-1 text-[10px] normal-case text-sky-700 dark:bg-sky-950/40 dark:text-sky-300">inherited</span> }
            </dt>
            <dd class="mt-0.5 break-words text-sm font-medium text-gray-900 dark:text-white">{{ show(field, data?.[field.key]) }}</dd>
          </div>
        }
      </dl>
    }
    @for (group of node.groups; track group.key) {
      <div class="mt-5 rounded-xl border border-gray-200 p-4 dark:border-gray-800">
        <p class="mb-3 text-sm font-semibold text-gray-900 dark:text-white">{{ labelOf(group) }}</p>
        <app-data-view [node]="group" [data]="child(group.key)" [inheritedPaths]="inheritedPaths" [path]="join(group.key)"></app-data-view>
      </div>
    }
    @for (coll of node.collections; track coll.key) {
      <div class="mt-5 rounded-xl border border-gray-200 p-4 dark:border-gray-800">
        <p class="mb-3 text-sm font-semibold text-gray-900 dark:text-white">{{ labelOf(coll) }} <span class="font-normal text-gray-500">· {{ items(coll.key).length }}</span></p>
        @for (item of items(coll.key); track item['$id']; let i = $index) {
          <details class="mb-2 rounded-lg border border-gray-100 px-3 py-2 dark:border-gray-800">
            <summary class="cursor-pointer text-sm text-gray-800 dark:text-gray-200">{{ itemTitle(coll, item, i) }}</summary>
            @if (itemNode(coll, item); as itemNodeSchema) {
              <div class="mt-3"><app-data-view [node]="itemNodeSchema" [data]="item" [inheritedPaths]="inheritedPaths" [path]="join(coll.key + '/' + item['$id'])"></app-data-view></div>
            }
          </details>
        } @empty { <p class="text-xs text-gray-400">No items.</p> }
      </div>
    }
    @for (nested of node.nestedNodes; track nested.key) {
      <div class="mt-5 rounded-xl border border-gray-200 p-4 dark:border-gray-800">
        @let value = child(nested.key);
        @let type = typeOf(nested, value);
        <p class="mb-3 text-sm font-semibold text-gray-900 dark:text-white">{{ labelOf(nested) }} <span class="font-normal text-gray-500">· {{ type ? typeLabel(type) : 'not set' }}</span></p>
        @if (type) { <app-data-view [node]="type.node" [data]="value" [inheritedPaths]="inheritedPaths" [path]="join(nested.key)"></app-data-view> }
      </div>
    }
  `,
})
export class DataViewComponent {
  @Input({ required: true }) node!: KVNodeSchema;
  @Input() data: DataObject | null = null;
  @Input() inheritedPaths: string[] = [];
  @Input() path = '';

  labelOf = labelOf;
  typeLabel = typeLabel;

  join(segment: string): string { return this.path ? `${this.path}/${segment}` : segment; }

  typeOf(nested: KVNestedNodeSchema, value: DataObject | null): KVItemTypeSchema | undefined {
    return value ? nested.types.find(t => t.token === value[TYPE_KEY]) : undefined;
  }

  child(key: string): DataObject | null { return (this.data?.[key] as DataObject | null) ?? null; }

  items(key: string): DataObject[] { return (this.data?.[key] as DataObject[]) ?? []; }

  isInherited(key: string): boolean {
    const path = this.join(key);
    return this.inheritedPaths.some(p => path === p || path.startsWith(`${p}/`));
  }

  itemNode(coll: KVCollectionSchema, item: DataObject): KVNodeSchema | null {
    return coll.itemTypes.find(t => t.token === item[TYPE_KEY])?.node ?? null;
  }

  itemTitle(coll: KVCollectionSchema, item: DataObject, index: number): string {
    const type = coll.itemTypes.find(t => t.token === item[TYPE_KEY]);
    const title = type ? titleField(type.node) : null;
    const name = title ? item[title.key] : null;
    return name != null && name !== '' ? String(name) : `${type ? typeLabel(type) : 'Item'} #${index + 1} (${String(item[ID_KEY]).slice(0, 8)})`;
  }

  show(field: KVFieldSchema, value: unknown): string {
    if (value == null || value === '') return '—';
    const options = optionsOf(field);
    const label = (v: unknown) => options.find(o => o.id === String(v))?.label ?? String(v);
    if (Array.isArray(value)) return value.length ? value.map(label).join(', ') : '—';
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    return label(value);
  }
}
