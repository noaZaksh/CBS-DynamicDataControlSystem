import { Component, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConditionNode, Operator, OperatorInfo, ValueSpec } from '../../../../core/models/models';
import { MetadataStore } from '../../../../core/services/metadata.store';
import { FieldPickerComponent } from '../field-picker/field-picker.component';
import { ValueInputComponent } from '../value-input/value-input.component';

/** שורת תנאי: [שדה] [אופרטור] [ערך]. האופרטורים והקלט נגזרים מסוג השדה במילון הנתונים. */
@Component({
  selector: 'app-condition-row',
  standalone: true,
  imports: [FormsModule, FieldPickerComponent, ValueInputComponent],
  templateUrl: './condition-row.component.html',
  styleUrl: './condition-row.component.scss',
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
