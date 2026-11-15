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
  templateUrl: './condition-group.component.html',
  styleUrl: './condition-group.component.scss',
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
