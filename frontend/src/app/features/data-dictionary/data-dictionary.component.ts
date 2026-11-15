import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DataType, EntityInfo, RelationInfo } from '../../core/models';
import { MetadataStore } from '../../core/metadata.store';

const TYPE_LABELS: Record<DataType, string> = { string: 'טקסט', number: 'מספר', date: 'תאריך', boolean: 'כן / לא' };
const TYPE_ICONS: Record<DataType, string> = { string: 'Aa', number: '#', date: '📅', boolean: '✓' };

/** מילון הנתונים במונחים עסקיים: אילו נתונים קיימים ואיך הם קשורים - בלי שמות טבלאות. */
@Component({
  selector: 'app-data-dictionary',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './data-dictionary.component.html',
  styleUrl: './data-dictionary.component.scss',
})
export class DataDictionaryComponent {
  private readonly metadata = inject(MetadataStore);
  readonly search = signal('');

  readonly entities = computed(() => {
    const q = this.search().trim();
    return this.metadata.entities()
      .map(e => (q ? { ...e, fields: e.fields.filter(f => f.displayName.includes(q) || (f.description ?? '').includes(q)) } : e))
      .filter(e => !q || e.fields.length);
  });

  typeLabel = (t: DataType) => TYPE_LABELS[t];
  typeIcon = (t: DataType) => TYPE_ICONS[t];

  describeRelation(e: EntityInfo, r: RelationInfo): string {
    const target = this.metadata.entity(r.targetEntity);
    return r.cardinality === 'ManyToOne'
      ? `${r.displayName} - לכל ${e.displayName} ${target?.displayName ?? ''} אחד`
      : `${r.displayName} - לכל ${e.displayName} יכולים להיות כמה ${target?.displayNamePlural ?? ''}`;
  }
}
