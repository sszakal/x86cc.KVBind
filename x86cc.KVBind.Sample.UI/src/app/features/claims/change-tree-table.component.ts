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

@Component({
  selector: 'app-change-tree-table',
  imports: [CommonModule],
  template: `
    @if (rows.length > 0) {
      <div class="overflow-x-auto rounded-xl border border-gray-200 dark:border-gray-800">
        <table class="w-full min-w-[520px] text-left text-sm">
          <thead class="border-b border-gray-100 bg-gray-50 text-xs uppercase text-gray-500 dark:border-gray-800 dark:bg-gray-900/60">
            <tr>
              <th class="px-4 py-3 font-medium">Path</th>
              <th class="px-4 py-3 font-medium">Change</th>
              <th class="px-4 py-3 font-medium">Old value</th>
              <th class="px-4 py-3 font-medium">New value</th>
              <th class="px-4 py-3 font-medium">Full path</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100 dark:divide-gray-800">
            @for (row of rows; track row.node.path) {
              <tr class="text-gray-700 dark:text-gray-300">
                <td class="px-4 py-3">
                  <div class="flex items-center gap-2" [style.padding-left.px]="row.depth * 18">
                    @if (row.node.children.length > 0) {
                      <button class="flex h-6 w-6 items-center justify-center rounded-md border border-gray-200 text-xs text-gray-500 hover:bg-gray-50 dark:border-gray-700 dark:hover:bg-gray-800" (click)="toggle(row.node.path)">
                        {{ expanded.has(row.node.path) ? '-' : '+' }}
                      </button>
                    } @else {
                      <span class="h-6 w-6"></span>
                    }
                    <span class="font-medium text-gray-900 dark:text-white">{{ row.node.label }}</span>
                  </div>
                </td>
                <td class="px-4 py-3">
                  @if (row.node.changeType) {
                    <span class="rounded-full px-2.5 py-1 text-xs font-medium" [ngClass]="badgeClass(row.node.changeType)">{{ row.node.changeType }}</span>
                  } @else {
                    <span class="text-xs text-gray-400">group</span>
                  }
                </td>
                <td class="max-w-56 px-4 py-3">
                  @if (row.node.changeType && hasValue(row.node.oldValue)) {
                    <span class="break-words text-sm text-red-600 line-through dark:text-red-300">{{ formatValue(row.node.oldValue) }}</span>
                  } @else if (row.node.changeType) {
                    <span class="text-xs text-gray-400">not set</span>
                  } @else {
                    <span class="text-xs text-gray-400">-</span>
                  }
                </td>
                <td class="max-w-56 px-4 py-3">
                  @if (row.node.changeType && hasValue(row.node.newValue)) {
                    <span class="break-words text-sm font-semibold text-emerald-700 dark:text-emerald-300">{{ formatValue(row.node.newValue) }}</span>
                  } @else if (row.node.changeType) {
                    <span class="text-xs text-gray-400">removed</span>
                  } @else {
                    <span class="text-xs text-gray-400">-</span>
                  }
                </td>
                <td class="px-4 py-3 font-mono text-xs text-gray-500">{{ row.node.path }}</td>
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
    const normalized = changeType.toLowerCase();
    if (normalized.includes('removed')) {
      return 'bg-red-50 text-red-700 dark:bg-red-950/30 dark:text-red-300';
    }

    if (normalized.includes('added')) {
      return 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-300';
    }

    return 'bg-blue-50 text-blue-700 dark:bg-blue-950/30 dark:text-blue-300';
  }

  hasValue(value: unknown): boolean {
    return value !== null && value !== undefined;
  }

  formatValue(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }

    if (typeof value === 'string') {
      return value;
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
      return String(value);
    }

    return JSON.stringify(value);
  }

  private buildTree(changes: ChangeTreeChange[]): ChangeTreeNode[] {
    const roots: ChangeTreeNode[] = [];
    const nodes = new Map<string, ChangeTreeNode>();

    for (const change of changes) {
      const normalizedPath = this.normalizePath(change.path);
      if (!normalizedPath) {
        continue;
      }

      const segments = normalizedPath.split('/').filter(segment => segment.length > 0);
      let parentChildren = roots;
      let currentPath = '';

      for (const segment of segments) {
        currentPath = currentPath ? `${currentPath}/${segment}` : segment;
        let node = nodes.get(currentPath);
        if (!node) {
          node = { label: segment, path: currentPath, changeType: '', children: [] };
          nodes.set(currentPath, node);
          parentChildren.push(node);
        }

        parentChildren = node.children;
      }

      const node = nodes.get(normalizedPath);
      if (node) {
        node.changeType = this.mergeChangeType(node.changeType, change.changeType);
        node.oldValue = change.oldValue ?? node.oldValue;
        node.newValue = change.newValue ?? node.newValue;
      }
    }

    return roots;
  }

  private normalizePath(path: string): string {
    const segments = path.split('/').filter(segment => segment.length > 0 && segment !== '$type' && segment !== '$id');
    return segments.join('/');
  }

  private mergeChangeType(existing: string, next: string): string {
    if (!existing) {
      return next;
    }

    const priority = ['Removed', 'Updated', 'Changed', 'Added'];
    const existingIndex = priority.findIndex(value => existing.toLowerCase().includes(value.toLowerCase()));
    const nextIndex = priority.findIndex(value => next.toLowerCase().includes(value.toLowerCase()));

    if (existingIndex < 0) {
      return next;
    }

    if (nextIndex < 0) {
      return existing;
    }

    return nextIndex < existingIndex ? next : existing;
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
