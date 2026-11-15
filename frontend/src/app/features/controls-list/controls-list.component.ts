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
  template: `
    <div class="page-header">
      <div>
        <h1>בקרות נתונים</h1>
        <p>בקרות שמאתרות שגיאות, חריגות וחוסר עקביות בנתונים העסקיים</p>
      </div>
      <div class="row">
        <a class="btn btn-ai" routerLink="/ai">✨ תיאור בקרה במילים</a>
        <a class="btn btn-primary" routerLink="/controls/new">+ בקרה חדשה</a>
      </div>
    </div>

    <div class="card filters">
      <div class="row">
        <input type="search" class="grow" placeholder="חיפוש לפי שם או תיאור..." [ngModel]="search()" (ngModelChange)="search.set($event)" aria-label="חיפוש בקרות" />
        <select [ngModel]="category()" (ngModelChange)="category.set($event)" aria-label="קטגוריה">
          <option value="">כל הקטגוריות</option>
          @for (c of categories(); track c) { <option [value]="c">{{ c }}</option> }
        </select>
        <select [ngModel]="severity()" (ngModelChange)="severity.set($event)" aria-label="חומרה">
          <option value="">כל רמות החומרה</option>
          <option value="high">גבוהה</option>
          <option value="medium">בינונית</option>
          <option value="low">נמוכה</option>
        </select>
      </div>
    </div>

    @if (loading()) {
      <div class="empty"><span class="spinner"></span> טוען בקרות...</div>
    } @else if (!filtered().length) {
      <div class="card empty">
        <span class="icon">🔍</span>
        @if (controls().length) { לא נמצאו בקרות התואמות לסינון } @else { עדיין לא הוגדרו בקרות }
      </div>
    } @else {
      <div class="list">
        @for (c of filtered(); track c.id) {
          <article class="card control">
            <div class="row head">
              <h3 class="spacer">
                <a [routerLink]="['/controls', c.id, 'edit']">{{ c.name }}</a>
              </h3>
              @if (c.source === 'ai') { <span class="badge badge-ai" title="נוצרה בעזרת AI">✨ AI</span> }
              <app-severity-badge [severity]="c.severity" />
            </div>
            <div class="meta small muted">
              <span>📂 {{ c.category || 'ללא קטגוריה' }}</span>
              <span>🗂 {{ c.rootEntityName }}</span>
              <span>גרסה {{ c.version }}</span>
            </div>
            @if (c.description) { <p class="desc">{{ c.description }}</p> }
            <div class="summary-box small">{{ c.summary }}</div>

            <div class="row foot">
              <div class="last-run small">
                @if (c.lastRun; as run) {
                  @if (run.status === 'success') {
                    <span class="badge" [class.badge-high]="run.exceptionCount" [class.badge-ok]="!run.exceptionCount">
                      {{ run.exceptionCount ? run.exceptionCount + ' חריגות' : 'ללא חריגות' }}
                    </span>
                  } @else {
                    <span class="badge badge-high" [title]="run.errorMessage ?? ''">הרצה נכשלה</span>
                  }
                  <span class="muted">הרצה אחרונה: {{ run.startedAt | date: 'dd/MM/yyyy HH:mm' }}</span>
                } @else {
                  <span class="muted">טרם הורצה</span>
                }
              </div>
              <span class="spacer"></span>
              <button class="btn btn-sm btn-ghost" (click)="duplicate(c)" title="יצירת בקרה חדשה על בסיס זו">⧉ שכפול</button>
              <a class="btn btn-sm btn-ghost" [routerLink]="['/controls', c.id, 'edit']">✎ עריכה</a>
              <button class="btn btn-sm btn-ghost btn-danger" (click)="remove(c)">🗑 מחיקה</button>
              <a class="btn btn-sm btn-primary" [routerLink]="['/controls', c.id, 'run']">▶ הפעלה</a>
            </div>
          </article>
        }
      </div>
    }
  `,
  styles: `
    .filters { margin-bottom: 16px; padding: 14px; }
    .grow { flex: 1 1 260px; }
    .list { display: grid; gap: 14px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 520px), 1fr)); }
    .control { display: flex; flex-direction: column; gap: 10px; margin: 0 !important; }
    .control h3 a { color: var(--text); }
    .control h3 a:hover { color: var(--primary); }
    .meta { display: flex; gap: 14px; flex-wrap: wrap; }
    .desc { margin: 0; }
    .foot { margin-top: auto; border-top: 1px solid var(--border); padding-top: 10px; }
    .last-run { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  `,
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
