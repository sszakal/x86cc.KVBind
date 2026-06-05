import { CommonModule } from '@angular/common';
import { Component, Input, OnChanges } from '@angular/core';

export interface ChangeTreeChange {
  path: string;
  changeType: string;
  oldValue?: unknown;
  newValue?: unknown;
}

interface ChangeTreeNode {
  label: string;
  path: string;
  changeType: string;
  oldValue?: unknown;
  newValue?: unknown;
  children: ChangeTreeNode[];
}

interface ChangeTreeRow {
  node: ChangeTreeNode;
  depth: number;
}

interface GuidDelta { added: number; removed: number; }

@Component({
  selector: 'app-change-tree-table',
  imports: [CommonModule],
  template: `
    @if (rows.length > 0) {
      <div class="overflow-x-auto rounded-xl border border-gray-200 dark:border-gray-800">
        <table class="w-full min-w-[480px] text-left text-sm">
          <thead class="border-b border-gray-100 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-900/60">
            <tr>
              <th class="px-4 py-3 font-medium">Field</th>
              <th class="px-4 py-3 font-medium">Change</th>
              <th class="px-4 py-3 font-medium">Old value</th>
              <th class="px-4 py-3 font-medium">New value</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100 dark:divide-gray-800">
            @for (row of rows; track row.node.path) {
              <tr class="text-gray-700 dark:text-gray-300">
                <!-- Field label -->
                <td class="px-4 py-3">
                  <div class="flex items-center gap-2" [style.padding-left.px]="row.depth * 18">
                    @if (row.node.children.length > 0) {
                      <button class="flex h-6 w-6 shrink-0 items-center justify-center rounded-md border border-gray-200 text-xs text-gray-500 hover:bg-gray-50 dark:border-gray-700 dark:hover:bg-gray-800"
                        (click)="toggle(row.node.path)">
                        {{ expanded.has(row.node.path) ? '−' : '+' }}
                      </button>
                    } @else {
                      <span class="h-6 w-6 shrink-0"></span>
                    }
                    <span class="font-medium text-gray-900 dark:text-white">{{ row.node.label }}</span>
                  </div>
                </td>
                <!-- Change type badge -->
                <td class="px-4 py-3">
                  @if (row.node.changeType) {
                    <span class="rounded-full px-2.5 py-1 text-xs font-medium" [ngClass]="badgeClass(row.node.changeType)">{{ row.node.changeType }}</span>
                  } @else {
                    <span class="text-xs text-gray-400">group</span>
                  }
                </td>
                <!-- Old value -->
                <td class="max-w-56 px-4 py-3">
                  @if (row.node.changeType && isGuidArray(row.node.oldValue)) {
                    <span class="text-sm text-red-500 line-through dark:text-red-400">{{ formatCount(row.node.oldValue) }}</span>
                  } @else if (row.node.changeType && hasValue(row.node.oldValue)) {
                    <span class="break-words text-sm text-red-600 line-through dark:text-red-300">{{ formatValue(row.node.oldValue) }}</span>
                  } @else if (row.node.changeType) {
                    <span class="text-xs text-gray-400">not set</span>
                  } @else {
                    <span class="text-xs text-gray-400">—</span>
                  }
                </td>
                <!-- New value -->
                <td class="max-w-56 px-4 py-3">
                  @if (row.node.changeType && isGuidArray(row.node.newValue)) {
                    @let delta = guidDelta(row.node.oldValue, row.node.newValue);
                    <div class="flex flex-wrap items-center gap-1.5">
                      <span class="text-sm font-semibold text-emerald-700 dark:text-emerald-300">{{ formatCount(row.node.newValue) }}</span>
                      @if (delta.added > 0) {
                        <span class="rounded px-1.5 py-0.5 font-mono text-xs font-semibold bg-emerald-50 text-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-300">+{{ delta.added }}</span>
                      }
                      @if (delta.removed > 0) {
                        <span class="rounded px-1.5 py-0.5 font-mono text-xs font-semibold bg-red-50 text-red-600 dark:bg-red-950/30 dark:text-red-300">−{{ delta.removed }}</span>
                      }
                    </div>
                  } @else if (row.node.changeType && hasValue(row.node.newValue)) {
                    <span class="break-words text-sm font-semibold text-emerald-700 dark:text-emerald-300">{{ formatValue(row.node.newValue) }}</span>
                  } @else if (row.node.changeType) {
                    <span class="text-xs text-gray-400">removed</span>
                  } @else {
                    <span class="text-xs text-gray-400">—</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @else {
      <p class="text-sm text-gray-500">No changes.</p>
    }
  `,
})
export class ChangeTreeTableComponent implements OnChanges {
  @Input() changes: ChangeTreeChange[] = [];

  roots: ChangeTreeNode[] = [];
  rows: ChangeTreeRow[] = [];
  expanded = new Set<string>();

  ngOnChanges(): void {
    this.roots = this.buildTree(this.changes);
    this.expanded = new Set<string>();
    this.expandNodes(this.roots);
    this.refreshRows();
  }

  toggle(path: string): void {
    if (this.expanded.has(path)) {
      this.expanded.delete(path);
    } else {
      this.expanded.add(path);
    }
    this.refreshRows();
  }

  badgeClass(changeType: string): string {
    const t = changeType.toLowerCase();
    if (t.includes('removed')) return 'bg-red-50 text-red-700 dark:bg-red-950/30 dark:text-red-300';
    if (t.includes('added'))   return 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-300';
    return 'bg-blue-50 text-blue-700 dark:bg-blue-950/30 dark:text-blue-300';
  }

  hasValue(value: unknown): boolean {
    return value !== null && value !== undefined;
  }

  isGuid(value: unknown): boolean {
    return typeof value === 'string'
      && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
  }

  isGuidArray(value: unknown): boolean {
    return Array.isArray(value) && (value.length === 0 || (value as unknown[]).every(v => this.isGuid(v)));
  }

  formatCount(value: unknown): string {
    const n = (value as unknown[]).length;
    return `${n} item${n !== 1 ? 's' : ''}`;
  }

  guidDelta(oldValue: unknown, newValue: unknown): GuidDelta {
    const oldSet = new Set<string>(Array.isArray(oldValue) ? (oldValue as string[]) : []);
    const newSet = new Set<string>(Array.isArray(newValue) ? (newValue as string[]) : []);
    let added = 0, removed = 0;
    for (const id of newSet) if (!oldSet.has(id)) added++;
    for (const id of oldSet) if (!newSet.has(id)) removed++;
    return { added, removed };
  }

  formatValue(value: unknown): string {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string') return value;
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    return JSON.stringify(value);
  }

  private buildTree(changes: ChangeTreeChange[]): ChangeTreeNode[] {
    const roots: ChangeTreeNode[] = [];
    const nodes = new Map<string, ChangeTreeNode>();
    // tracks how many UUID children have been created per parent path
    const guidCounters = new Map<string, number>();

    for (const change of changes) {
      const normalizedPath = this.normalizePath(change.path);
      if (!normalizedPath) continue;

      const segments = normalizedPath.split('/').filter(s => s.length > 0);
      let parentChildren = roots;
      let currentPath = '';

      for (const segment of segments) {
        const parentPath = currentPath;
        currentPath = currentPath ? `${currentPath}/${segment}` : segment;

        let node = nodes.get(currentPath);
        if (!node) {
          let label: string;
          if (this.isGuid(segment)) {
            const n = (guidCounters.get(parentPath) ?? 0) + 1;
            guidCounters.set(parentPath, n);
            label = `Item #${n}`;
          } else {
            label = segment;
          }
          node = { label, path: currentPath, changeType: '', children: [] };
          nodes.set(currentPath, node);
          parentChildren.push(node);
        }

        parentChildren = node.children;
      }

      const leaf = nodes.get(normalizedPath);
      if (leaf) {
        leaf.changeType = this.mergeChangeType(leaf.changeType, change.changeType);
        leaf.oldValue = change.oldValue ?? leaf.oldValue;
        leaf.newValue = change.newValue ?? leaf.newValue;
      }
    }

    return roots;
  }

  private normalizePath(path: string): string {
    return path.split('/').filter(s => s.length > 0 && s !== '$type' && s !== '$id').join('/');
  }

  private mergeChangeType(existing: string, next: string): string {
    if (!existing) return next;
    const priority = ['Removed', 'Updated', 'Changed', 'Added'];
    const ei = priority.findIndex(v => existing.toLowerCase().includes(v.toLowerCase()));
    const ni = priority.findIndex(v => next.toLowerCase().includes(v.toLowerCase()));
    if (ei < 0) return next;
    if (ni < 0) return existing;
    return ni < ei ? next : existing;
  }

  private expandNodes(nodes: ChangeTreeNode[]): void {
    for (const node of nodes) {
      if (node.children.length > 0) {
        this.expanded.add(node.path);
        this.expandNodes(node.children);
      }
    }
  }

  private refreshRows(): void {
    const rows: ChangeTreeRow[] = [];
    this.appendRows(this.roots, rows, 0);
    this.rows = rows;
  }

  private appendRows(nodes: ChangeTreeNode[], rows: ChangeTreeRow[], depth: number): void {
    for (const node of nodes) {
      rows.push({ node, depth });
      if (node.children.length > 0 && this.expanded.has(node.path)) {
        this.appendRows(node.children, rows, depth + 1);
      }
    }
  }
}
