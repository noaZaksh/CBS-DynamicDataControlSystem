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
  template: `
    <div class="page-header">
      <div>
        <h1>מילון נתונים</h1>
        <p>הנתונים שעליהם ניתן להגדיר בקרות, והקשרים ביניהם - במונחים עסקיים</p>
      </div>
      <input type="search" placeholder="חיפוש שדה..." [ngModel]="search()" (ngModelChange)="search.set($event)" aria-label="חיפוש שדה" />
    </div>

    <div class="entities">
      @for (e of entities(); track e.key) {
        <section class="card entity">
          <div class="card-title">
            <h2>{{ e.displayNamePlural }}</h2>
            <span class="badge">{{ e.fields.length }} שדות</span>
          </div>
          @if (e.description) { <p class="muted small">{{ e.description }}</p> }

          <ul class="fields">
            @for (f of e.fields; track f.key) {
              <li [title]="f.description ?? ''">
                <span class="type" [attr.aria-label]="typeLabel(f.dataType)">{{ typeIcon(f.dataType) }}</span>
                <span class="spacer">{{ f.displayName }}@if (f.isBusinessRequired) {<span class="req" title="שדה חובה עסקי"> *</span>}</span>
                <span class="small muted">{{ typeLabel(f.dataType) }}</span>
                @if (f.allowedValues?.length) { <span class="small muted">({{ f.allowedValues!.join(' / ') }})</span> }
              </li>
            }
          </ul>

          @if (e.relations.length) {
            <h3>קשרים</h3>
            <ul class="relations">
              @for (r of e.relations; track r.key) {
                <li>🔗 {{ describeRelation(e, r) }}</li>
              }
            </ul>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .entities { display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 380px), 1fr)); align-items: start; }
    .entity { margin: 0 !important; }
    .fields, .relations { list-style: none; margin: 0; padding: 0; }
    .fields li { display: flex; align-items: center; gap: 8px; padding: 6px 0; border-bottom: 1px solid var(--border); }
    .fields li:last-child { border-bottom: 0; }
    .type { display: inline-grid; place-items: center; width: 26px; height: 22px; border-radius: 6px; background: var(--surface-2); font-size: .75rem; font-weight: 700; color: var(--muted); }
    .req { color: var(--danger); }
    h3 { margin: 14px 0 6px; font-size: .95rem; }
    .relations li { padding: 3px 0; font-size: .92rem; }
  `,
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
