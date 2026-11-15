import { ConditionNode, ControlDefinition, FilterNode, GroupNode, RelatedNode } from './models';

let counter = 0;

/** מזהה ייחודי לצומת חדש (השרת מנרמל מזהים בכל מקרה). */
export function newId(prefix: string): string {
  counter = (counter + 1) % 100000;
  return `${prefix}${Date.now().toString(36).slice(-4)}${counter}`;
}

export function newCondition(): ConditionNode {
  return { type: 'condition', id: newId('c'), field: '', operator: 'eq', value: { kind: 'constant', value: null } };
}

export function newGroup(logic: 'and' | 'or' = 'and', children: FilterNode[] = []): GroupNode {
  return { type: 'group', id: newId('g'), logic, negate: false, children };
}

export function newRelated(relation: string): RelatedNode {
  return { type: 'related', id: newId('r'), relation, quantifier: 'any', filter: newGroup('and', [newCondition()]) };
}

export function emptyDefinition(rootEntity = ''): ControlDefinition {
  return { rootEntity, filter: newGroup('and', [newCondition()]) };
}

export function cloneDefinition(def: ControlDefinition): ControlDefinition {
  return structuredClone(def);
}

/** מספר התנאים בעץ (לתצוגה). */
export function countConditions(node: FilterNode): number {
  switch (node.type) {
    case 'group': return node.children.reduce((sum, c) => sum + countConditions(c), 0);
    case 'related': return 1;
    default: return 1;
  }
}
