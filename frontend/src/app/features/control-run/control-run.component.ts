import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Control, ControlRunSummary, RunResult } from '../../core/models';
import { describeError } from '../../core/notification.service';
import { ResultsTableComponent } from '../../shared/results-table.component';
import { SeverityBadgeComponent } from '../../shared/severity-badge.component';

/** הפעלת בקרה והצגת התוצאות: כמות חריגות, טבלת רשומות עם סיבת חריגה, והיסטוריית הרצות. */
@Component({
  selector: 'app-control-run',
  standalone: true,
  imports: [RouterLink, DatePipe, DecimalPipe, PercentPipe, ResultsTableComponent, SeverityBadgeComponent],
  template: `
    <div class="page-header">
      <div>
        <a routerLink="/controls" class="small">→ חזרה לרשימת הבקרות</a>
        <h1>{{ control()?.name ?? 'הפעלת בקרה' }}</h1>
        @if (control(); as c) { <p>{{ c.description }}</p> }
      </div>
      <div class="row">
        @if (control(); as c) {
          <app-severity-badge [severity]="c.severity" />
          <a class="btn" [routerLink]="['/controls', c.id, 'edit']">✎ עריכה</a>
        }
        <button class="btn btn-primary" (click)="run()" [disabled]="running()">
          @if (running()) { <span class="spinner"></span> מריץ... } @else { ▶ הרצה מחדש }
        </button>
      </div>
    </div>

    @if (error(); as e) {
      <div class="alert alert-error">
        <strong>{{ e.message }}</strong>
        @if (e.details.length) { <ul>@for (d of e.details; track $index) { <li>{{ d }}</li> }</ul> }
      </div>
    }

    @if (result(); as r) {
      <div class="grid grid-4 stats">
        <div class="card stat">
          <span class="value" [class.bad]="r.exceptionCount">{{ r.exceptionCount | number }}</span>
          <span class="label">רשומות חריגות</span>
        </div>
        <div class="card stat">
          <span class="value">{{ r.totalRecords | number }}</span>
          <span class="label">{{ r.entityName }} נבדקו</span>
        </div>
        <div class="card stat">
          <span class="value">{{ ratio() | percent: '1.0-1' }}</span>
          <span class="label">שיעור חריגות</span>
        </div>
        <div class="card stat">
          <span class="value">{{ r.durationMs | number }} <small>ms</small></span>
          <span class="label">זמן ריצה · {{ r.startedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</span>
        </div>
      </div>

      <div class="summary-box small rule"><strong>הבקרה מאתרת:</strong> {{ r.summary }}</div>

      @if (r.truncated) {
        <div class="alert alert-warning">מוצגות {{ r.rows.length | number }} הרשומות הראשונות מתוך {{ r.exceptionCount | number }}.</div>
      }

      <section class="card">
        @if (r.rows.length) {
          <app-results-table [columns]="r.columns" [rows]="r.rows" [fileName]="'control-' + id()" />
        } @else {
          <div class="empty"><span class="icon">✅</span>לא נמצאו חריגות - כל הרשומות עומדות בבקרה</div>
        }
      </section>
    } @else if (running()) {
      <div class="empty"><span class="spinner"></span> מריץ את הבקרה...</div>
    }

    @if (history().length) {
      <section class="card">
        <h2 class="card-title">היסטוריית הרצות</h2>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>מועד</th><th>גרסה</th><th>סטטוס</th><th>חריגות</th><th>נבדקו</th><th>משך (ms)</th></tr></thead>
            <tbody>
              @for (h of history(); track h.runId) {
                <tr>
                  <td>{{ h.startedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                  <td>{{ h.controlVersion }}</td>
                  <td>
                    @if (h.status === 'success') { <span class="badge badge-ok">הצליחה</span> }
                    @else { <span class="badge badge-high" [title]="h.errorMessage ?? ''">נכשלה</span> }
                  </td>
                  <td class="num">{{ h.exceptionCount ?? '—' }}</td>
                  <td class="num">{{ h.totalRecords ?? '—' }}</td>
                  <td class="num">{{ h.durationMs }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
  `,
  styles: `
    .stats { margin-bottom: 14px; }
    @media (max-width: 640px) { .stats { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; } .stats .card { padding: 12px; } }
    .stats .card { margin: 0 !important; }
    .value.bad { color: var(--danger); }
    .rule { margin-bottom: 14px; }
    .alert { margin-bottom: 14px; }
  `,
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
