import { Component, computed, input } from '@angular/core';
import { Severity } from '../../core/models/models';

const LABELS: Record<Severity, string> = { high: 'חומרה גבוהה', medium: 'חומרה בינונית', low: 'חומרה נמוכה' };

@Component({
  selector: 'app-severity-badge',
  standalone: true,
  template: `<span class="badge" [class]="'badge badge-' + severity()">{{ label() }}</span>`,
})
export class SeverityBadgeComponent {
  readonly severity = input.required<Severity>();
  readonly label = computed(() => LABELS[this.severity()] ?? this.severity());
}
