import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { DataModel, DataType, EntityInfo, FieldInfo, OperatorInfo, RelationInfo } from './models';

/** אפשרות בבורר השדות: שדה של הישות עצמה או שדה דרך קשר ("מחלקה פעילה (המחלקה של העובד)"). */
export interface FieldOption {
  path: string;
  label: string;
  group: string;
  field: FieldInfo;
}

const MAX_HOPS = 2;

/**
 * מאגר מילון הנתונים (Signals). נטען פעם אחת ומשמש את כל המסכים.
 * כל הממשק - בחירת נתונים, שדות, אופרטורים ושדות קלט - נגזר ממנו, ולכן גנרי לחלוטין.
 */
@Injectable({ providedIn: 'root' })
export class MetadataStore {
  private readonly api = inject(ApiService);
  private readonly model = signal<DataModel | null>(null);
  private loading: Promise<DataModel> | null = null;

  readonly loaded = computed(() => this.model() !== null);
  readonly entities = computed(() => this.model()?.entities ?? []);
  readonly operators = computed(() => this.model()?.operators ?? []);

  load(): Promise<DataModel> {
    const current = this.model();
    if (current) return Promise.resolve(current);
    this.loading ??= firstValueFrom(this.api.getDataModel())
      .then(m => { this.model.set(m); return m; })
      .finally(() => (this.loading = null));
    return this.loading;
  }

  entity(key: string | undefined | null): EntityInfo | undefined {
    return this.entities().find(e => e.key === key);
  }

  operator(op: string): OperatorInfo | undefined {
    return this.operators().find(o => o.operator === op);
  }

  operatorsFor(type: DataType | undefined): OperatorInfo[] {
    return type ? this.operators().filter(o => o.appliesTo.includes(type)) : [];
  }

  operatorLabel(op: OperatorInfo, type: DataType | undefined): string {
    return type === 'date' && op.dateLabel ? op.dateLabel : op.label;
  }

  /** כל השדות שאפשר לבדוק מישות מסוימת: שלה + דרך קשרי "רבים-לאחד" (עד 2 קפיצות). */
  fieldOptions(entityKey: string): FieldOption[] {
    const result: FieldOption[] = [];
    const walk = (entity: EntityInfo, prefix: string, labels: string[], hops: number) => {
      const group = labels.length ? labels.join(' › ') : entity.displayName;
      for (const field of entity.fields) {
        result.push({
          path: prefix + field.key,
          label: labels.length ? `${field.displayName} (${labels.join(' › ')})` : field.displayName,
          group,
          field,
        });
      }
      if (hops >= MAX_HOPS) return;
      for (const rel of entity.relations.filter(r => r.cardinality === 'ManyToOne')) {
        const target = this.entity(rel.targetEntity);
        if (target) walk(target, `${prefix}${rel.key}.`, [...labels, rel.displayName], hops + 1);
      }
    };
    const root = this.entity(entityKey);
    if (root) walk(root, '', [], 0);
    return result;
  }

  findField(entityKey: string, path: string | undefined): FieldOption | undefined {
    return path ? this.fieldOptions(entityKey).find(f => f.path === path) : undefined;
  }

  /** קשרים "אחד-לרבים" - לתנאי "קיים / לא קיים". */
  manyRelations(entityKey: string): RelationInfo[] {
    return this.entity(entityKey)?.relations.filter(r => r.cardinality === 'OneToMany') ?? [];
  }

  relation(entityKey: string, relationKey: string): RelationInfo | undefined {
    return this.entity(entityKey)?.relations.find(r => r.key === relationKey);
  }
}
