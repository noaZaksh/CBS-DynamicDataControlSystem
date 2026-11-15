import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OperatorInfo, ValueSpec } from '../../core/models';
import { FieldOption } from '../../core/metadata.store';
import { FieldPickerComponent } from './field-picker.component';

/**
 * שדה ערך שמותאם אוטומטית לסוג הנתון:
 * מספר / תאריך / כן-לא / טקסט / רשימה סגורה, ובנוסף "שדה אחר" ו"היום" כשזה רלוונטי.
 */
@Component({
  selector: 'app-value-input',
  standalone: true,
  imports: [FormsModule, FieldPickerComponent],
  template: `
    <div class="value">
      @if (modes().length > 1) {
        <select class="mode" [ngModel]="spec().kind" (ngModelChange)="setKind($event)" aria-label="סוג ערך">
          <option value="constant">ערך</option>
          @if (canCompareField()) { <option value="field">שדה אחר</option> }
          @if (field().field.dataType === 'date') { <option value="today">יחסית להיום</option> }
        </select>
      }

      @switch (spec().kind) {
        @case ('field') {
          <app-field-picker [entityKey]="entityKey()" [value]="spec().field ?? ''" [onlyType]="field().field.dataType"
                            [exclude]="field().path" placeholder="בחרו שדה להשוואה..." (valueChange)="patch({ field: $event })" />
        }
        @case ('today') {
          <div class="row nowrap">
            <span>היום</span>
            <select [ngModel]="sign()" (ngModelChange)="setOffset($event, absOffset())" aria-label="כיוון">
              <option value="+">+</option><option value="-">−</option>
            </select>
            <input type="number" min="0" class="days" [ngModel]="absOffset()" (ngModelChange)="setOffset(sign(), $event)" aria-label="ימים" />
            <span>ימים</span>
          </div>
        }
        @case ('list') {
          <input type="text" [ngModel]="listText()" (ngModelChange)="setList($event)" placeholder="ערכים מופרדים בפסיק" aria-label="רשימת ערכים" />
        }
        @default {
          @switch (field().field.dataType) {
            @case ('boolean') {
              <select [ngModel]="spec().value" (ngModelChange)="patch({ value: $event })" aria-label="ערך">
                <option [ngValue]="true">כן</option>
                <option [ngValue]="false">לא</option>
              </select>
            }
            @case ('number') {
              <input type="number" [ngModel]="spec().value" (ngModelChange)="patch({ value: $event })" placeholder="מספר" aria-label="ערך" />
            }
            @case ('date') {
              <input type="date" [ngModel]="spec().value" (ngModelChange)="patch({ value: $event })" aria-label="תאריך" />
            }
            @default {
              @if (field().field.allowedValues?.length) {
                <select [ngModel]="spec().value" (ngModelChange)="patch({ value: $event })" aria-label="ערך">
                  <option [ngValue]="null" disabled>בחרו ערך...</option>
                  @for (v of field().field.allowedValues; track v) { <option [ngValue]="v">{{ v }}</option> }
                </select>
              } @else {
                <input type="text" [ngModel]="spec().value" (ngModelChange)="patch({ value: $event })" placeholder="טקסט" aria-label="ערך" />
              }
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .value { display: flex; gap: 6px; align-items: center; min-width: 0; }
    .value > :last-child { flex: 1; min-width: 0; }
    .mode { flex: 0 0 auto; }
    .days { width: 80px; }
    .nowrap { flex-wrap: nowrap; }
  `,
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
