import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { newCondition, newGroup, newRelated } from '../../core/definition-utils';
import { ConditionNode, FilterNode, GroupNode, RelatedNode } from '../../core/models';
import { MetadataStore } from '../../core/metadata.store';
import { ConditionRowComponent } from './condition-row.component';

/**
 * קבוצת תנאים רקורסיבית: "וגם"/"או", שלילה, תתי-קבוצות ותנאי "קיים / לא קיים" על רשומות קשורות.
 * הקומפוננטה מציגה את עצמה מחדש עבור קבוצות מקוננות - כך שאין הגבלה מבנית בממשק.
 */
@Component({
  selector: 'app-condition-group',
  standalone: true,
  imports: [FormsModule, ConditionRowComponent, ConditionGroupComponent],
  template: `
    <div class="group" [class.root]="depth() === 0" [class.or]="group().logic === 'or'">
      <div class="group-head row">
        @if (group().children.length > 1) {
          <div class="logic" role="radiogroup" aria-label="חיבור בין התנאים">
            <button type="button" [class.on]="group().logic === 'and'" (click)="setLogic('and')">כל התנאים (וגם)</button>
            <button type="button" [class.on]="group().logic === 'or'" (click)="setLogic('or')">לפחות אחד (או)</button>
          </div>
        } @else {
          <span class="muted small">התנאי:</span>
        }
        <label class="negate small" title="הופך את משמעות הקבוצה">
          <input type="checkbox" [ngModel]="!!group().negate" (ngModelChange)="setNegate($event)" /> לא מתקיים
        </label>
        <span class="spacer"></span>
        @if (depth() > 0 && removable()) {
          <button class="icon-btn" (click)="remove.emit()" title="הסרת קבוצה" aria-label="הסרת קבוצה">✕</button>
        }
      </div>

      <div class="children">
        @for (child of group().children; track trackNode($index, child); let i = $index) {
          @if (i > 0) { <div class="joiner">{{ group().logic === 'and' ? 'וגם' : 'או' }}</div> }

          @switch (child.type) {
            @case ('condition') {
              <app-condition-row [node]="asCondition(child)" [entityKey]="entityKey()" [errorIds]="errorIds()"
                                 (changed)="changed.emit()" (remove)="removeAt(i)" />
            }
            @case ('group') {
              <app-condition-group [group]="asGroup(child)" [entityKey]="entityKey()" [depth]="depth() + 1" [errorIds]="errorIds()"
                                   (changed)="changed.emit()" (remove)="removeAt(i)" />
            }
            @case ('related') {
              <div class="related" [class.has-error]="child.id && errorIds().has(child.id)">
                <div class="row">
                  <select [ngModel]="asRelated(child).quantifier" (ngModelChange)="setQuantifier(asRelated(child), $event)" aria-label="קיים / לא קיים">
                    <option value="any">קיימת לפחות רשומה אחת ב</option>
                    <option value="none">לא קיימת אף רשומה ב</option>
                  </select>
                  <select [ngModel]="asRelated(child).relation" (ngModelChange)="setRelation(asRelated(child), $event)" aria-label="קשר">
                    @for (r of manyRelations(); track r.key) { <option [value]="r.key">{{ r.displayName }}</option> }
                  </select>
                  <span class="muted small">שעומדת בתנאים:</span>
                  <span class="spacer"></span>
                  <button class="icon-btn" (click)="removeAt(i)" title="הסרה" aria-label="הסרת תנאי קשר">✕</button>
                </div>
                @if (asRelated(child).filter; as inner) {
                  <app-condition-group [group]="inner" [entityKey]="targetOf(asRelated(child))" [depth]="depth() + 1"
                                       [removable]="false" [errorIds]="errorIds()" (changed)="changed.emit()" />
                }
              </div>
            }
          }
        }
      </div>

      <div class="actions row">
        <button class="btn btn-sm" (click)="add('condition')">+ תנאי</button>
        @if (depth() < 3) { <button class="btn btn-sm" (click)="add('group')">+ קבוצת תנאים</button> }
        @if (manyRelations().length && depth() < 2) {
          <button class="btn btn-sm" (click)="add('related')" title="למשל: מחלקה שאין בה אף עובד פעיל">+ תנאי על רשומות קשורות</button>
        }
      </div>
    </div>
  `,
  styles: `
    .group { border: 1px solid var(--border); border-inline-start: 4px solid var(--primary); border-radius: 8px; padding: 12px; background: var(--surface); }
    .group.or { border-inline-start-color: var(--warning); }
    .group.root { border-width: 0; border-inline-start-width: 0; padding: 0; background: transparent; }
    .group-head { margin-bottom: 10px; }
    .logic { display: inline-flex; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
    .logic button { border: 0; background: var(--surface); padding: 5px 12px; font: inherit; cursor: pointer; color: var(--muted); }
    .logic button.on { background: var(--primary); color: #fff; font-weight: 600; }
    .or .logic button.on { background: var(--warning); }
    .negate { display: inline-flex; align-items: center; gap: 4px; color: var(--muted); }
    .children { display: flex; flex-direction: column; gap: 6px; }
    .joiner { font-size: .78rem; font-weight: 700; color: var(--muted); padding-inline-start: 8px; }
    .related { border: 1px dashed var(--ai); border-radius: 8px; padding: 10px; background: var(--ai-soft); display: flex; flex-direction: column; gap: 8px; }
    .related.has-error { border-color: var(--danger); }
    .actions { margin-top: 10px; }
  `,
})
export class ConditionGroupComponent {
  private readonly metadata = inject(MetadataStore);

  readonly group = input.required<GroupNode>();
  readonly entityKey = input.required<string>();
  readonly depth = input(0);
  readonly removable = input(true);
  readonly errorIds = input<Set<string>>(new Set());
  readonly changed = output<void>();
  readonly remove = output<void>();

  readonly manyRelations = computed(() => this.metadata.manyRelations(this.entityKey()));

  /** מזהה יציב לרינדור; צומת חדש שעוד אין לו מזהה - לפי מיקום */
  trackNode = (index: number, node: FilterNode) => node.id ?? `i${index}`;

  asCondition = (n: FilterNode) => n as ConditionNode;
  asGroup = (n: FilterNode) => n as GroupNode;
  asRelated = (n: FilterNode) => n as RelatedNode;

  targetOf(node: RelatedNode): string {
    return this.metadata.relation(this.entityKey(), node.relation)?.targetEntity ?? '';
  }

  setLogic(logic: 'and' | 'or') {
    this.group().logic = logic;
    this.changed.emit();
  }

  setNegate(value: boolean) {
    this.group().negate = value;
    this.changed.emit();
  }

  setQuantifier(node: RelatedNode, q: 'any' | 'none') {
    node.quantifier = q;
    this.changed.emit();
  }

  setRelation(node: RelatedNode, relation: string) {
    node.relation = relation;
    node.filter = newGroup('and', [newCondition()]);   // ישות היעד השתנתה - התנאים הפנימיים מתאפסים
    this.changed.emit();
  }

  add(kind: 'condition' | 'group' | 'related') {
    const node: FilterNode =
      kind === 'condition' ? newCondition() :
      kind === 'group' ? newGroup(this.group().logic === 'and' ? 'or' : 'and', [newCondition(), newCondition()]) :
      newRelated(this.manyRelations()[0].key);
    this.group().children.push(node);
    this.changed.emit();
  }

  removeAt(index: number) {
    this.group().children.splice(index, 1);
    this.changed.emit();
  }
}
