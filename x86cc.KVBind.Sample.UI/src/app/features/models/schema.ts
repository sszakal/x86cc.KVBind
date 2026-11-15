// A model's schema as the API serves it: the x86cc.KVBind.Schema document, generated server-side from the
// real KVBind definition. These interfaces mirror the package's POCO classes (the API sends enum kinds as
// names). The helpers below read the document; no field, label or option is written by hand in the UI.

export type KVSchemaValueKind =
  | 'Object' | 'String' | 'Boolean' | 'Integer' | 'Decimal' | 'Number'
  | 'Date' | 'DateTime' | 'Time' | 'Duration' | 'Guid' | 'Enum';

export interface KVAnnotationSchema { key: string; value: string | null; kind: KVSchemaValueKind; }

export interface KVPlaceholderSchema { name: string; label: string; kind: KVSchemaValueKind; isRequired: boolean; }

export interface KVAllowedValueSchema { id: string; label: string; template: string | null; placeholders: KVPlaceholderSchema[]; }

export interface KVAllowedValuesSchema { appliesToElements: boolean; options: KVAllowedValueSchema[]; }

export interface KVFieldSchema {
  key: string;
  label: string | null;
  kind: KVSchemaValueKind;
  isArray: boolean;
  isNullable: boolean;
  enumMembers: string[];
  isFlags: boolean;
  isRequired: boolean;
  isReadOnly: boolean;
  isInherited: boolean;
  hasDefault: boolean;
  defaultValue: string | null;
  defaultValues: string[];
  minLength: number | null;
  maxLength: number | null;
  pattern: string | null;
  min: string | null;
  max: string | null;
  allowedValues: KVAllowedValuesSchema | null;
  annotations: KVAnnotationSchema[];
}

// `id` is set on a type that occurs again inside itself (a recursive definition); an inner occurrence
// carries `ref` to that id and an empty node. resolveReferences() links them once, after loading.
export interface KVItemTypeSchema { token: string; label: string | null; id: string | null; ref: string | null; node: KVNodeSchema; }

export interface KVAggregateRuleSchema { fieldKey: string; comparison: string; threshold: number; errorCode: string; }

export interface KVCollectionSchema {
  key: string;
  label: string | null;
  isReadOnly: boolean;
  isInherited: boolean;
  notEmpty: boolean;
  minCount: number | null;
  maxCount: number | null;
  allowedOperations: string[] | null;
  operations: string[];
  itemTypes: KVItemTypeSchema[];
  seeds: Array<{ itemId: string; typeToken: string }>;
  aggregateRules: KVAggregateRuleSchema[];
  annotations: KVAnnotationSchema[];
}

export interface KVNestedNodeSchema {
  key: string;
  label: string | null;
  isReadOnly: boolean;
  isInherited: boolean;
  isRequired: boolean;
  defaultType: string | null;
  types: KVItemTypeSchema[];
  annotations: KVAnnotationSchema[];
}

export interface KVNodeSchema {
  key: string;
  label: string | null;
  isReadOnly: boolean;
  isInherited: boolean;
  isResettable: boolean;
  fields: KVFieldSchema[];
  groups: KVNodeSchema[];
  collections: KVCollectionSchema[];
  nestedNodes: KVNestedNodeSchema[];
  operations: string[];
  annotations: KVAnnotationSchema[];
}

export interface KVSchemaDocument { model: string; schemaVersion: number; root: KVNodeSchema; }

// Points every `ref` item type at the node of the type it names, so a recursive schema reads as a tree of
// any depth (the object graph becomes cyclic; it is only read, never serialized again).
export function resolveReferences(schema: KVSchemaDocument): KVSchemaDocument {
  const byId = new Map<string, KVItemTypeSchema>();
  const refs: KVItemTypeSchema[] = [];
  const visit = (node: KVNodeSchema): void => {
    node.groups.forEach(visit);
    const types = [...node.collections.flatMap(c => c.itemTypes), ...node.nestedNodes.flatMap(n => n.types)];
    for (const type of types) {
      if (type.id) byId.set(type.id, type);
      if (type.ref) refs.push(type);
      else visit(type.node);
    }
  };
  visit(schema.root);
  for (const type of refs) {
    const target = byId.get(type.ref!);
    if (target) type.node = target.node;
  }
  return schema;
}

// ── The application's UI vocabulary, declared in the DSL as annotations (see UiAnnotations.cs) ─────────

export const UI_CONTROL = 'ui:control';
export const UI_ROLE = 'ui:role';
export const UI_HELP = 'ui:help';
export const UI_TITLE = 'ui:title';

export function annotation(annotations: KVAnnotationSchema[], key: string): string | null {
  return annotations.find(a => a.key === key)?.value ?? null;
}

export function labelOf(member: { key: string; label: string | null }): string {
  return member.label ?? humanize(member.key);
}

export function typeLabel(type: KVItemTypeSchema): string {
  return type.label ?? humanize(type.token);
}

// The control a field renders with: the DSL's ui:control annotation, else one that fits its kind.
export function controlOf(field: KVFieldSchema): string {
  const declared = annotation(field.annotations, UI_CONTROL);
  if (declared) return declared;
  if (field.allowedValues) return field.allowedValues.appliesToElements || field.isArray ? 'multiselect' : 'select';
  if (field.kind === 'Enum') return field.isFlags || field.isArray ? 'multiselect' : 'select';
  if (field.isArray) return 'list';
  switch (field.kind) {
    case 'Integer': case 'Decimal': case 'Number': return 'number';
    case 'Boolean': return 'checkbox';
    case 'Date': return 'date';
    case 'DateTime': return 'datetime';
    case 'Time': return 'time';
    default: return 'text';
  }
}

// The options a select shows: declared allowed values, or the members of an enum.
export function optionsOf(field: KVFieldSchema): KVAllowedValueSchema[] {
  if (field.allowedValues) return field.allowedValues.options;
  if (field.kind === 'Enum') {
    return field.enumMembers.map(member => ({ id: member, label: humanize(member), template: null, placeholders: [] }));
  }
  return [];
}

// Plain-language constraint hints, e.g. "max 1000 chars", "1–5".
export function constraintHints(field: KVFieldSchema): string[] {
  const hints: string[] = [];
  if (field.minLength != null && field.maxLength != null) hints.push(`${field.minLength}–${field.maxLength} chars`);
  else if (field.minLength != null) hints.push(`min ${field.minLength} chars`);
  else if (field.maxLength != null) hints.push(`max ${field.maxLength} chars`);
  if (field.min != null && field.max != null) hints.push(`${field.min} – ${field.max}`);
  else if (field.min != null) hints.push(`≥ ${field.min}`);
  else if (field.max != null) hints.push(`≤ ${field.max}`);
  if (field.pattern) hints.push(`pattern ${field.pattern}`);
  if (field.hasDefault) hints.push(`default ${field.defaultValue ?? field.defaultValues.join(', ')}`);
  return hints;
}

// The field that names a collection item in lists: ui:title, else the first text field.
export function titleField(node: KVNodeSchema): KVFieldSchema | null {
  return node.fields.find(f => annotation(f.annotations, UI_TITLE) != null)
    ?? node.fields.find(f => f.kind === 'String' && !f.isArray)
    ?? null;
}

// Whether a collection lets clients send this structural operation (null allowlist = everything).
export function allows(collection: KVCollectionSchema, operation: string): boolean {
  return !collection.isReadOnly
    && (collection.allowedOperations == null || collection.allowedOperations.includes(operation));
}

// RESET of a node that holds a PatchReadOnly member anywhere beneath it is rejected (it would wipe it), so the
// UI hides the button. Mirrors the check KVBind makes: fields, groups, collections and their item types,
// nested nodes and their types. Types can be recursive, so each node is visited once.
export function hasReadOnlyMembers(node: KVNodeSchema, visited = new Set<KVNodeSchema>()): boolean {
  if (visited.has(node)) return false;
  visited.add(node);
  return node.fields.some(f => f.isReadOnly)
    || node.groups.some(g => g.isReadOnly || hasReadOnlyMembers(g, visited))
    || node.collections.some(c => c.isReadOnly || c.itemTypes.some(t => hasReadOnlyMembers(t.node, visited)))
    || node.nestedNodes.some(n => n.isReadOnly || n.types.some(t => hasReadOnlyMembers(t.node, visited)));
}

// "FullName" -> "Full Name", "PERSON" -> "Person", "policy_number" -> "Policy Number".
export function humanize(key: string): string {
  return key
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .split(' ')
    .filter(word => word.length > 0)
    .map(word => word === word.toUpperCase() && word.length > 1
      ? word[0] + word.slice(1).toLowerCase()
      : word[0].toUpperCase() + word.slice(1))
    .join(' ');
}

// ── Reading the projected data (see AggregateDataProjector.cs) ────────────────────────────────────────

export type DataObject = Record<string, unknown>;
export const ID_KEY = '$id';
export const TYPE_KEY = '$type';

export function joinPath(prefix: string, segment: string): string {
  return prefix ? `${prefix}/${segment}` : segment;
}

// Reads the value at a data path: collections are arrays of items addressed by their $id.
export function valueAt(data: DataObject | null | undefined, path: string): unknown {
  let current: unknown = data;
  for (const segment of path.split('/').filter(s => s.length > 0)) {
    if (current == null) return null;
    if (Array.isArray(current)) {
      current = current.find(item => (item as DataObject)[ID_KEY] === segment) ?? null;
    } else {
      current = (current as DataObject)[segment];
    }
  }
  return current ?? null;
}

// A located member of the schema, resolved from a data path.
export type Located =
  | { kind: 'node'; path: string; node: KVNodeSchema; label: string; role: 'root' | 'group' | 'item' | 'nested'; data: DataObject; readOnly: boolean }
  | { kind: 'collection'; path: string; collection: KVCollectionSchema; label: string; items: DataObject[]; readOnly: boolean }
  | { kind: 'nestedSlot'; path: string; nested: KVNestedNodeSchema; label: string; data: DataObject | null; readOnly: boolean };

// Walks schema and data together to the node, collection or nested slot at `path`. A nested slot that holds
// a subtype resolves to that subtype's node when `enterNested` is set.
export function locate(schema: KVSchemaDocument, data: DataObject, path: string, enterNested = false): Located | null {
  let located: Located = { kind: 'node', path: '', node: schema.root, label: 'Overview', role: 'root', data, readOnly: schema.root.isReadOnly };
  const segments = path.split('/').filter(s => s.length > 0);

  for (let index = 0; index < segments.length; index++) {
    const segment = segments[index];
    const current: Located = located;
    const childPath = joinPath(current.path, segment);
    const next: Located | null = current.kind === 'collection'
      ? enterItem(current, segment, childPath)
      : current.kind === 'node'
        ? enterMember(current, segment, childPath, index === segments.length - 1, enterNested)
        : null;
    if (!next) return null;
    located = next;
  }

  return located;
}

type LocatedNode = Extract<Located, { kind: 'node' }>;
type LocatedCollection = Extract<Located, { kind: 'collection' }>;

function enterItem(current: LocatedCollection, itemId: string, childPath: string): Located | null {
  const item = current.items.find(i => i[ID_KEY] === itemId);
  const type = item ? current.collection.itemTypes.find(t => t.token === item[TYPE_KEY]) : undefined;
  if (!item || !type) return null;
  const position = current.items.indexOf(item) + 1;
  return { kind: 'node', path: childPath, node: type.node, label: `${typeLabel(type)} #${position}`, role: 'item', data: item, readOnly: current.readOnly };
}

function enterMember(current: LocatedNode, segment: string, childPath: string, isLast: boolean, enterNested: boolean): Located | null {
  const { node, readOnly, data } = current;

  const group = node.groups.find(g => g.key === segment);
  if (group) {
    return { kind: 'node', path: childPath, node: group, label: labelOf(group), role: 'group', data: (data[segment] as DataObject) ?? {}, readOnly: readOnly || group.isReadOnly };
  }

  const collection = node.collections.find(c => c.key === segment);
  if (collection) {
    return { kind: 'collection', path: childPath, collection, label: labelOf(collection), items: (data[segment] as DataObject[]) ?? [], readOnly: readOnly || collection.isReadOnly };
  }

  const nested = node.nestedNodes.find(n => n.key === segment);
  if (!nested) return null;

  const value = (data[segment] as DataObject | null) ?? null;
  const slotReadOnly = readOnly || nested.isReadOnly;
  const type = value ? nested.types.find(t => t.token === value[TYPE_KEY]) : undefined;
  if (isLast && !enterNested) {
    return { kind: 'nestedSlot', path: childPath, nested, label: labelOf(nested), data: value, readOnly: slotReadOnly };
  }
  return type && value
    ? { kind: 'node', path: childPath, node: type.node, label: labelOf(nested), role: 'nested', data: value, readOnly: slotReadOnly }
    : null;
}

// Finds the field schema for a field path (used by the create form), searching groups by path.
export function fieldAt(schema: KVSchemaDocument, path: string): KVFieldSchema | null {
  const segments = path.split('/');
  let node: KVNodeSchema | undefined = schema.root;
  for (const segment of segments.slice(0, -1)) {
    node = node?.groups.find(g => g.key === segment);
  }
  return node?.fields.find(f => f.key === segments[segments.length - 1]) ?? null;
}
