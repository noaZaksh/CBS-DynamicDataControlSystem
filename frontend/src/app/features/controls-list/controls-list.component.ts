import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Control } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { SeverityBadgeComponent } from '../../shared/severity-badge.component';

/** מסך ראשי: רשימת הבקרות, חיפוש וסינון, והפעלה ישירה מהרשימה. */
@Component({
  selector: 'app-controls-list',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, SeverityBadgeComponent],
  templateUrl: './controls-list.component.html',
  styleUrl: './controls-list.component.scss',
})
export class ControlsListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotificationService);
  private readonly router = inject(Router);

  readonly controls = signal<Control[]>([]);
  readonly loading = signal(true);
  readonly search = signal('');
  readonly category = signal('');
  readonly severity = signal('');

  readonly categories = computed(() =>
    [...new Set(this.controls().map(c => c.category).filter((c): c is string => !!c))].sort());

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.controls().filter(c =>
      (!this.category() || c.category === this.category()) &&
      (!this.severity() || c.severity === this.severity()) &&
      (!q || `${c.name} ${c.description ?? ''} ${c.summary}`.toLowerCase().includes(q)));
  });

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.listControls().subscribe({
      next: list => { this.controls.set(list); this.loading.set(false); },
      error: err => { this.loading.set(false); this.notify.error(err, 'טעינת הבקרות נכשלה'); },
    });
  }

  duplicate(c: Control) {
    this.api.duplicateControl(c.id).subscribe({
      next: copy => {
        this.notify.success(`נוצר עותק: ${copy.name}`);
        this.router.navigate(['/controls', copy.id, 'edit']);
      },
      error: err => this.notify.error(err, 'השכפול נכשל'),
    });
  }

  remove(c: Control) {
    if (!confirm(`למחוק את הבקרה "${c.name}"?`)) return;
    this.api.deleteControl(c.id).subscribe({
      next: () => {
        this.controls.update(list => list.filter(x => x.id !== c.id));
        this.notify.success('הבקרה נמחקה');
      },
      error: err => this.notify.error(err, 'המחיקה נכשלה'),
    });
  }
}
