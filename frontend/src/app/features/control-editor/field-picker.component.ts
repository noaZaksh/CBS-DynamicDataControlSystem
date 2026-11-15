import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DataType } from '../../core/models';
import { FieldOption, MetadataStore } from '../../core/metadata.store';

/** בורר שדה עסקי - מקובץ לפי ישות/קשר ("המחלקה של העובד"). ניתן לסנן לפי סוג נתון. */
@Component({
  selector: 'app-field-picker',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './field-picker.component.html',
  styleUrl: './field-picker.component.scss',
})
export class FieldPickerComponent {
  private readonly metadata = inject(MetadataStore);

  readonly entityKey = input.required<string>();
  readonly value = input<string>('');
  readonly onlyType = input<DataType | undefined>(undefined);
  readonly exclude = input<string | undefined>(undefined);
  readonly placeholder = input('בחרו שדה...');
  readonly valueChange = output<string>();

  readonly groups = computed(() => {
    const map = new Map<string, FieldOption[]>();
    for (const o of this.metadata.fieldOptions(this.entityKey())) {
      if (this.onlyType() && o.field.dataType !== this.onlyType()) continue;
      if (o.path === this.exclude()) continue;
      if (!map.has(o.group)) map.set(o.group, []);
      map.get(o.group)!.push(o);
    }
    return [...map.entries()].map(([name, options]) => ({ name, options }));
  });
}
