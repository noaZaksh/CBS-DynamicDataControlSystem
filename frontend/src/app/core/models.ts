// ---------------------------------------------------------------------------
// מודלים התואמים את ה-API (.NET). הגדרת הבקרה זהה לפורמט שנשמר ב-DB ושמוחזר מה-AI.
// ---------------------------------------------------------------------------

export type DataType = 'string' | 'number' | 'date' | 'boolean';
export type Arity = 'none' | 'single' | 'range' | 'list';
export type Severity = 'low' | 'medium' | 'high';

export interface FieldInfo {
  key: string;
  displayName: string;
  description?: string;
  dataType: DataType;
  isBusinessRequired: boolean;
  allowedValues?: string[];
}

export interface RelationInfo {
  key: string;
  displayName: string;
  description?: string;
  targetEntity: string;
  cardinality: 'ManyToOne' | 'OneToMany';
}

export interface EntityInfo {
  key: string;
  displayName: string;
  displayNamePlural: string;
  description?: string;
  titleFieldKey?: string;
  fields: FieldInfo[];
  relations: RelationInfo[];
}

export interface OperatorInfo {
  operator: Operator;
  label: string;
  dateLabel?: string;
  arity: Arity;
  appliesTo: DataType[];
  allowsFieldComparison: boolean;
}

export interface DataModel {
  entities: EntityInfo[];
  operators: OperatorInfo[];
}

// ---------------------------------------------------------------- definition

export type Operator =
  | 'eq' | 'neq' | 'gt' | 'gte' | 'lt' | 'lte' | 'between' | 'in' | 'notIn'
  | 'contains' | 'notContains' | 'startsWith' | 'endsWith' | 'isEmpty' | 'isNotEmpty';

export type ValueKind = 'constant' | 'list' | 'field' | 'today';

export interface ValueSpec {
  kind: ValueKind;
  value?: string | number | boolean | null;
  values?: (string | number)[];
  field?: string;
  offsetDays?: number;
}

export interface GroupNode {
  type: 'group';
  id?: string;
  logic: 'and' | 'or';
  negate?: boolean;
  children: FilterNode[];
}

export interface ConditionNode {
  type: 'condition';
  id?: string;
  field: string;
  operator: Operator;
  value?: ValueSpec;
  valueTo?: ValueSpec;
}

export interface RelatedNode {
  type: 'related';
  id?: string;
  relation: string;
  quantifier: 'any' | 'none';
  filter?: GroupNode;
}

export type FilterNode = GroupNode | ConditionNode | RelatedNode;

export interface ControlDefinition {
  schemaVersion?: number;
  rootEntity: string;
  filter: GroupNode;
}

// ---------------------------------------------------------------- controls

export interface ControlRunSummary {
  runId: number;
  controlId: number;
  controlVersion: number;
  startedAt: string;
  durationMs: number;
  status: 'success' | 'failed';
  totalRecords?: number;
  exceptionCount?: number;
  errorMessage?: string;
}

export interface Control {
  id: number;
  name: string;
  description?: string;
  category?: string;
  severity: Severity;
  source: 'manual' | 'ai';
  rootEntity: string;
  rootEntityName: string;
  definition: ControlDefinition;
  summary: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  lastRun?: ControlRunSummary;
}

export interface SaveControlRequest {
  name: string;
  description?: string;
  category?: string;
  severity: Severity;
  source?: 'manual' | 'ai';
  definition: ControlDefinition;
}

export interface ValidationError {
  nodeId?: string;
  message: string;
}

export interface ValidateResponse {
  isValid: boolean;
  errors: ValidationError[];
  summary?: string;
  generatedSql?: string;
  parameters?: { name: string; type: string; value: unknown }[];
  normalizedDefinition?: ControlDefinition;
}

// ---------------------------------------------------------------- run results

export interface ResultColumn {
  key: string;
  label: string;
  dataType: DataType;
  role: 'title' | 'display' | 'evidence';
}

export interface ResultRow {
  key: string;
  title?: string;
  values: Record<string, string | number | boolean | null>;
  reasons: string[];
  matchedNodeIds: string[];
}

export interface RunResult {
  controlId?: number;
  controlName?: string;
  runId?: number;
  entityName: string;
  summary: string;
  startedAt: string;
  durationMs: number;
  totalRecords: number;
  exceptionCount: number;
  truncated: boolean;
  columns: ResultColumn[];
  rows: ResultRow[];
}

// ---------------------------------------------------------------- AI

export interface AiDraft {
  name: string;
  description?: string;
  category?: string;
  severity: Severity;
  definition?: ControlDefinition;
  summary?: string;
  isValid: boolean;
  validationErrors: ValidationError[];
  confidence: number;
  explanation?: string;
  assumptions: string[];
  warnings: string[];
  unmatchedTerms: string[];
  provider?: string;
  model?: string;
}

/** ProblemDetails (RFC 7807) כפי שמוחזר מה-API */
export interface ApiProblem {
  title?: string;
  status?: number;
  errors?: ValidationError[];
  traceId?: string;
}
