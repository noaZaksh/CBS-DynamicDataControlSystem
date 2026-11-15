import { DecimalPipe, PercentPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DraftTransferService } from '../../core/draft-transfer.service';
import { AiDraft, RunResult } from '../../core/models';
import { NotificationService, describeError } from '../../core/notification.service';
import { ResultsTableComponent } from '../../shared/results-table.component';
import { SeverityBadgeComponent } from '../../shared/severity-badge.component';

const EXAMPLES = [
  'מצא עובדים פעילים שגילם נמוך מ-18 ומשכורתם גבוהה מ-15,000 ש"ח',
  'איתור עובדים שתאריך סיום העסקתם מוקדם מתאריך תחילת העסקתם',
  'איתור עובדים פעילים המשויכים למחלקה שאינה פעילה',
  'עובדים פעילים שחסרה להם תעודת זהות או דוא"ל',
  'מחלקות פעילות ללא עובדים פעילים',
  'עובדים פעילים המשובצים לפרויקט שהסתיים',
];

/**
 * שלב ב': הגדרת בקרה בשפה חופשית.
 * התיאור נשלח ל-AI (דרך .NET → שירות Python), התשובה מאומתת ומוצגת כטיוטה לאישור.
 * שום דבר לא נשמר או מורץ ללא פעולה מפורשת של המשתמש.
 */
@Component({
  selector: 'app-ai-assistant',
  standalone: true,
  imports: [FormsModule, DecimalPipe, PercentPipe, ResultsTableComponent, SeverityBadgeComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>✨ הגדרת בקרה בשפה חופשית</h1>
        <p>תארו במילים מה צריך לבדוק. ה-AI יציע הגדרת בקרה, ואתם תאשרו אותה לפני השמירה וההרצה.</p>
      </div>
      <div class="tabs" role="tablist">
        <button role="tab" [class.on]="tab() === 'text'" (click)="tab.set('text')">תיאור חופשי</button>
        <button role="tab" [class.on]="tab() === 'suggest'" (click)="tab.set('suggest'); loadSuggestions()">הצעות יזומות</button>
      </div>
    </div>

    @if (tab() === 'text') {
      <section class="card">
        <label class="field">מה הבקרה צריכה למצוא?
          <textarea [(ngModel)]="text" rows="3" maxlength="1000" placeholder='לדוגמה: מצא עובדים פעילים שגילם נמוך מ-18 ומשכורתם גבוהה מ-15,000 ש"ח'
                    (keydown.control.enter)="interpret()"></textarea>
        </label>
        <div class="row examples">
          <span class="small muted">דוגמאות:</span>
          @for (e of examples; track e) { <button class="chip" (click)="text = e; interpret()">{{ e }}</button> }
        </div>
        <div class="row">
          <span class="small muted">{{ text.length }}/1000 · Ctrl+Enter לשליחה</span>
          <span class="spacer"></span>
          <button class="btn btn-ai" (click)="interpret()" [disabled]="!text.trim() || thinking()">
            @if (thinking()) { <span class="spinner"></span> מפענח... } @else { ✨ הצע בקרה }
          </button>
        </div>
      </section>

      @if (error(); as e) { <div class="alert alert-error">{{ e }}</div> }

      @if (draft(); as d) {
        <section class="card draft">
          <div class="card-title">
            <div>
              <h2>{{ d.name }}</h2>
              <span class="small muted">הצעה של {{ d.provider }}{{ d.model ? ' · ' + d.model : '' }}</span>
            </div>
            <div class="row">
              <app-severity-badge [severity]="d.severity" />
              <div class="confidence" [title]="'רמת ביטחון: ' + (d.confidence | percent)">
                <div class="bar" [style.width.%]="d.confidence * 100" [class.low]="d.confidence < .6"></div>
              </div>
              <span class="small">{{ d.confidence | percent }} ביטחון</span>
            </div>
          </div>

          @if (d.isValid) {
            <div class="alert alert-ai"><strong>הבנתי שצריך למצוא:</strong> {{ d.summary }}</div>
          } @else {
            <div class="alert alert-error">
              <strong>לא ניתן ליצור בקרה תקינה מהתיאור</strong>
              <ul>@for (e of d.validationErrors; track $index) { <li>{{ e.message }}</li> }</ul>
            </div>
          }

          <div class="grid grid-2 info">
            @if (d.explanation) {
              <div><h3>איך פוענח הטקסט</h3><pre class="explain">{{ d.explanation }}</pre></div>
            }
            <div class="stack">
              @if (d.assumptions.length) {
                <div><h3>הנחות</h3><ul>@for (a of d.assumptions; track $index) { <li>{{ a }}</li> }</ul></div>
              }
              @if (d.warnings.length) {
                <div class="alert alert-warning"><strong>שימו לב</strong><ul>@for (w of d.warnings; track $index) { <li>{{ w }}</li> }</ul></div>
              }
              @if (d.unmatchedTerms.length) {
                <div class="small muted">ביטויים שלא זוהו: {{ d.unmatchedTerms.join(', ') }}</div>
              }
            </div>
          </div>

          @if (d.isValid) {
            <div class="row actions">
              <button class="btn" (click)="testRun(d)" [disabled]="busy()">🧪 הרצת ניסיון</button>
              <span class="spacer"></span>
              <button class="btn" (click)="edit(d)">✎ עריכה לפני שמירה</button>
              <button class="btn btn-primary" (click)="approve(d)" [disabled]="busy()">✓ אישור, שמירה והפעלה</button>
            </div>
          }
        </section>

        @if (preview(); as p) {
          <section class="card">
            <div class="card-title">
              <h2>תוצאות הרצת ניסיון</h2>
              <span class="badge" [class.badge-high]="p.exceptionCount" [class.badge-ok]="!p.exceptionCount">
                {{ p.exceptionCount | number }} חריגות מתוך {{ p.totalRecords | number }}
              </span>
            </div>
            @if (p.rows.length) { <app-results-table [columns]="p.columns" [rows]="p.rows" fileName="ai-preview" /> }
            @else { <div class="empty">לא נמצאו חריגות</div> }
          </section>
        }
      }
    } @else {
      <section class="card">
        <p class="muted">ה-AI ניתח את מבנה הנתונים (שדות חובה, זוגות תאריכים, דגלי "פעיל" בקשרים, אחוזים וסכומים) והציע בקרות איכות נתונים:</p>
        @if (thinking()) { <div class="empty"><span class="spinner"></span> מנתח את מבנה הנתונים...</div> }
        <div class="suggestions">
          @for (s of suggestions(); track $index) {
            <article class="suggestion">
              <div class="row">
                <strong class="spacer">{{ s.name }}</strong>
                <app-severity-badge [severity]="s.severity" />
              </div>
              <div class="small">{{ s.summary }}</div>
              @if (s.explanation) { <div class="small muted">💡 {{ s.explanation }}</div> }
              <div class="row">
                <span class="badge">{{ s.category }}</span>
                <span class="spacer"></span>
                <button class="btn btn-sm" (click)="edit(s)" [disabled]="!s.isValid">✎ בדיקה ושמירה</button>
              </div>
            </article>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .tabs { display: inline-flex; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
    .tabs button { border: 0; background: var(--surface); padding: 8px 16px; font: inherit; cursor: pointer; color: var(--muted); font-weight: 600; }
    .tabs button.on { background: var(--ai); color: #fff; }
    textarea { width: 100%; font-size: 1.05rem; }
    .examples { margin: 10px 0 12px; }
    .draft { border-color: var(--ai); }
    .confidence { width: 90px; height: 8px; background: var(--surface-2); border-radius: 99px; overflow: hidden; border: 1px solid var(--border); }
    .confidence .bar { height: 100%; background: var(--success); }
    .confidence .bar.low { background: var(--warning); }
    .info { margin-top: 14px; }
    .info h3 { margin-bottom: 6px; font-size: .95rem; }
    .info ul { margin: 0; padding-inline-start: 18px; }
    pre.explain { white-space: pre-wrap; direction: rtl; text-align: right; font-family: inherit; background: var(--surface-2); padding: 10px; border-radius: 8px; margin: 0; font-size: .88rem; }
    .actions { margin-top: 16px; border-top: 1px solid var(--border); padding-top: 12px; }
    .suggestions { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 380px), 1fr)); }
    .suggestion { border: 1px solid var(--border); border-radius: 10px; padding: 12px; display: flex; flex-direction: column; gap: 8px; }
  `,
})
export class AiAssistantComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotificationService);
  private readonly drafts = inject(DraftTransferService);

  readonly examples = EXAMPLES;
  text = '';

  readonly tab = signal<'text' | 'suggest'>('text');
  readonly draft = signal<AiDraft | null>(null);
  readonly suggestions = signal<AiDraft[]>([]);
  readonly preview = signal<RunResult | null>(null);
  readonly thinking = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  interpret() {
    const text = this.text.trim();
    if (!text) return;
    this.thinking.set(true);
    this.error.set(null);
    this.preview.set(null);
    this.api.interpret(text).subscribe({
      next: d => { this.draft.set(d); this.thinking.set(false); },
      error: err => { this.thinking.set(false); this.draft.set(null); this.error.set(describeError(err, 'שירות ה-AI אינו זמין').message); },
    });
  }

  loadSuggestions() {
    if (this.suggestions().length) return;
    this.thinking.set(true);
    this.api.aiSuggestions(20).subscribe({
      next: s => { this.suggestions.set(s.filter(x => x.isValid)); this.thinking.set(false); },
      error: err => { this.thinking.set(false); this.notify.error(err, 'שירות ה-AI אינו זמין'); },
    });
  }

  testRun(d: AiDraft) {
    this.busy.set(true);
    this.api.runPreview(d.definition!).subscribe({
      next: r => { this.preview.set(r); this.busy.set(false); },
      error: err => { this.busy.set(false); this.notify.error(err, 'הרצת הניסיון נכשלה'); },
    });
  }

  /** מעבר לעורך הרגיל עם הטיוטה - המשתמש רואה ומתקן כל תנאי לפני שמירה. */
  edit(d: AiDraft) {
    this.drafts.set(this.toRequest(d));
    this.router.navigate(['/controls/new']);
  }

  approve(d: AiDraft) {
    this.busy.set(true);
    this.api.createControl(this.toRequest(d)).subscribe({
      next: saved => {
        this.busy.set(false);
        this.notify.success('הבקרה נשמרה');
        this.router.navigate(['/controls', saved.id, 'run']);
      },
      error: err => { this.busy.set(false); this.notify.error(err, 'השמירה נכשלה'); },
    });
  }

  private toRequest(d: AiDraft) {
    return {
      name: d.name,
      description: d.description ?? this.text,
      category: d.category,
      severity: d.severity,
      source: 'ai' as const,
      definition: d.definition!,
    };
  }
}
