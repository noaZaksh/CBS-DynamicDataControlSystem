import { Component, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConditionNode, Operator, OperatorInfo, ValueSpec } from '../../core/models';
import { MetadataStore } from '../../core/metadata.store';
import { FieldPickerComponent } from './field-picker.component';
import { ValueInputComponent } from './value-input.component';

/** שורת תנאי: [שדה] [אופרטור] [ערך]. האופרטורים והקלט נגזרים מסוג השדה במילון הנתונים. */
@Component({
  selector: 'app-condition-row',
  standalone: true,
  imports: [FormsModule, FieldPickerComponent, ValueInputComponent],
  template: `
    <div class="condition" [class.has-error]="hasError()">
      <div class="field-col">
        <app-field-picker [entityKey]="entityKey()" [value]="node().field" (valueChange)="setField($event)" />
      </div>

      <div class="op-col">
        <select [ngModel]="node().operator" (ngModelChange)="setOperator($event)" [disabled]="!field()" aria-label="השוואה">
          @for (op of operators(); track op.operator) {
            <option [value]="op.operator">{{ label(op) }}</option>
          }
        </select>
      </div>

      <div class="value-col">
        @if (field() && operator(); as op) {
          @if (op.arity === 'single' || op.arity === 'list') {
            <app-value-input [entityKey]="entityKey()" [field]="field()!" [operator]="op"
                             [spec]="node().value ?? defaultSpec()" (specChange)="setValue('value', $event)" />
          } @else if (op.arity === 'range') {
            <div class="range">
              <app-value-input [entityKey]="entityKey()" [field]="field()!" [operator]="op"
                               [spec]="node().value ?? defaultSpec()" (specChange)="setValue('value', $event)" />
              <span>עד</span>
              <app-value-input [entityKey]="entityKey()" [field]="field()!" [operator]="op"
                               [spec]="node().valueTo ?? defaultSpec()" (specChange)="setValue('valueTo', $event)" />
            </div>
          }
        }
      </div>

      <button class="icon-btn" (click)="remove.emit()" title="הסרת תנאי" aria-label="הסרת תנאי">✕</button>
    </div>
    @if (field()?.field?.description) {
      <div class="hint small muted">ℹ {{ field()!.field.description }}</div>
    }
  `,
  styles: `
    .condition { display: grid; grid-template-columns: minmax(180px, 1.3fr) minmax(130px, .9fr) minmax(180px, 1.5fr) auto; gap: 8px; align-items: center; }
    .condition select, .condition input { width: 100%; }
    .has-error select:first-child { border-color: var(--danger); }
    .range { display: flex; align-items: center; gap: 6px; }
    .range > app-value-input { flex: 1; min-width: 0; }
    .hint { margin: 2px 4px 0; }
    @media (max-width: 760px) {
      .condition { grid-template-columns: 1fr auto; }
      .op-col, .value-col { grid-column: 1; }
      .icon-btn { grid-row: 1; grid-column: 2; }
    }
  `,
})
export class ConditionRowComponent {
  private readonly metadata = inject(MetadataStore);

  readonly node = input.required<ConditionNode>();
  readonly entityKey = input.required<string>();
  readonly errorIds = input<Set<string>>(new Set());
  readonly changed = output<void>();
  readonly remove = output<void>();

  // הצומת משתנה במקום (mutable), לכן אלו מתודות ולא computed - הן מחושבות מחדש בכל רינדור
  field() { return this.metadata.findField(this.entityKey(), this.node().field); }
  operators() { return this.metadata.operatorsFor(this.field()?.field.dataType); }
  operator() { return this.metadata.operator(this.node().operator); }
  hasError() { return !!this.node().id && this.errorIds().has(this.node().id!); }

  label(op: OperatorInfo) {
    return this.metadata.operatorLabel(op, this.field()?.field.dataType);
  }

  defaultSpec(): ValueSpec {
    const op = this.operator();
    if (op?.arity === 'list') return { kind: 'list', values: [] };
    return { kind: 'constant', value: this.field()?.field.dataType === 'boolean' ? true : null };
  }

  setField(path: string) {
    const node = this.node();
    node.field = path;
    const type = this.metadata.findField(this.entityKey(), path)?.field.dataType;
    const allowed = this.metadata.operatorsFor(type);
    if (!allowed.some(o => o.operator === node.operator)) node.operator = allowed[0]?.operator ?? 'eq';
    this.resetValues(node);
    this.changed.emit();
  }

  setOperator(op: Operator) {
    const node = this.node();
    const before = this.metadata.operator(node.operator)?.arity;
    node.operator = op;
    if (this.metadata.operator(op)?.arity !== before) this.resetValues(node);
    this.changed.emit();
  }

  setValue(which: 'value' | 'valueTo', spec: ValueSpec) {
    this.node()[which] = spec;
    this.changed.emit();
  }

  private resetValues(node: ConditionNode) {
    const arity = this.metadata.operator(node.operator)?.arity;
    delete node.valueTo;
    if (arity === 'none') delete node.value;
    else node.value = this.defaultSpec();
    if (arity === 'range') node.valueTo = this.defaultSpec();
  }
}
