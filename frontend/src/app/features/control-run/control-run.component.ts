import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { Control, ControlRunSummary, RunResult } from '../../core/models/models';
import { describeError } from '../../core/services/notification.service';
import { ResultsTableComponent } from '../../shared/results-table/results-table.component';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

/** הפעלת בקרה והצגת התוצאות: כמות חריגות, טבלת רשומות עם סיבת חריגה, והיסטוריית הרצות. */
@Component({
  selector: 'app-control-run',
  standalone: true,
  imports: [RouterLink, DatePipe, DecimalPipe, PercentPipe, ResultsTableComponent, SeverityBadgeComponent],
  templateUrl: './control-run.component.html',
  styleUrl: './control-run.component.scss',
})
export class ControlRunComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly id = input.required<string>();
  readonly control = signal<Control | null>(null);
  readonly result = signal<RunResult | null>(null);
  readonly history = signal<ControlRunSummary[]>([]);
  readonly running = signal(false);
  readonly error = signal<{ message: string; details: string[] } | null>(null);

  readonly ratio = computed(() => {
    const r = this.result();
    return r && r.totalRecords ? r.exceptionCount / r.totalRecords : 0;
  });

  ngOnInit() {
    this.api.getControl(+this.id()).subscribe({
      next: c => this.control.set(c),
      error: err => this.error.set(describeError(err, 'הבקרה לא נמצאה')),
    });
    this.run();
  }

  run() {
    this.running.set(true);
    this.error.set(null);
    this.api.runControl(+this.id()).subscribe({
      next: r => { this.result.set(r); this.running.set(false); this.loadHistory(); },
      error: err => { this.running.set(false); this.error.set(describeError(err, 'הרצת הבקרה נכשלה')); this.loadHistory(); },
    });
  }

  private loadHistory() {
    this.api.getRuns(+this.id()).subscribe({ next: h => this.history.set(h), error: () => {} });
  }
}
