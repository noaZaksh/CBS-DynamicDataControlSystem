import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OperatorInfo, ValueSpec } from '../../../../core/models/models';
import { FieldOption } from '../../../../core/services/metadata.store';
import { FieldPickerComponent } from '../field-picker/field-picker.component';

/**
 * שדה ערך שמותאם אוטומטית לסוג הנתון:
 * מספר / תאריך / כן-לא / טקסט / רשימה סגורה, ובנוסף "שדה אחר" ו"היום" כשזה רלוונטי.
 */
@Component({
  selector: 'app-value-input',
  standalone: true,
  imports: [FormsModule, FieldPickerComponent],
  templateUrl: './value-input.component.html',
  styleUrl: './value-input.component.scss',
})
export class ValueInputComponent {
  readonly entityKey = input.required<string>();
  readonly field = input.required<FieldOption>();
  readonly operator = input.required<OperatorInfo>();
  readonly spec = input.required<ValueSpec>();
  readonly specChange = output<ValueSpec>();

  readonly canCompareField = computed(() => this.operator().allowsFieldComparison);
  readonly modes = computed(() => {
    if (this.operator().arity !== 'single') return ['constant'];
    const m = ['constant'];
    if (this.canCompareField()) m.push('field');
    if (this.field().field.dataType === 'date') m.push('today');
    return m;
  });

  readonly listText = computed(() => (this.spec().values ?? []).join(', '));
  readonly sign = computed(() => ((this.spec().offsetDays ?? 0) < 0 ? '-' : '+'));
  readonly absOffset = computed(() => Math.abs(this.spec().offsetDays ?? 0));

  setKind(kind: ValueSpec['kind']) {
    const next: ValueSpec =
      kind === 'field' ? { kind, field: '' } :
      kind === 'today' ? { kind, offsetDays: 0 } :
      { kind: 'constant', value: this.field().field.dataType === 'boolean' ? true : null };
    this.specChange.emit(next);
  }

  setOffset(sign: string, days: number | null) {
    const n = Math.abs(Number(days) || 0);
    this.patch({ offsetDays: sign === '-' ? -n : n });
  }

  setList(text: string) {
    const isNumber = this.field().field.dataType === 'number';
    const values = text.split(',').map(s => s.trim()).filter(Boolean).map(s => (isNumber && !isNaN(+s) ? +s : s));
    this.patch({ values });
  }

  patch(change: Partial<ValueSpec>) {
    this.specChange.emit({ ...this.spec(), ...change });
  }
}
