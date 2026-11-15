import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { cloneDefinition, emptyDefinition } from '../../core/definition-utils';
import { DraftTransferService } from '../../core/draft-transfer.service';
import { MetadataStore } from '../../core/metadata.store';
import { ControlDefinition, RunResult, SaveControlRequest, Severity, ValidateResponse } from '../../core/models';
import { NotificationService, describeError } from '../../core/notification.service';
import { ResultsTableComponent } from '../../shared/results-table.component';
import { ConditionGroupComponent } from './condition-group.component';

const CATEGORY_SUGGESTIONS = ['שלמות נתונים', 'תאריכים', 'שכר', 'שיוך ארגוני', 'פרויקטים', 'טווח ערכים', 'עקביות סטטוס'];

/**
 * עורך בקרה (יצירה ועריכה). המשתמש בוחר נתונים עסקיים ובונה תנאים בעזרת רכיבים ויזואליים;
 * בכל שינוי ההגדרה נשלחת לבדיקה בשרת, שמחזיר תיאור בעברית, שגיאות ואת ה-SQL שייווצר.
 */
@Component({
  selector: 'app-control-editor',
  standalone: true,
  imports: [FormsModule, RouterLink, ConditionGroupComponent, ResultsTableComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ isEdit() ? 'עריכת בקרה' : 'בקרה חדשה' }}</h1>
        <p>הגדירו אילו רשומות נחשבות <strong>חריגות</strong>: כל רשומה שעומדת בתנאים תוצג בתוצאות הבקרה</p>
      </div>
      @if (fromAi()) { <span class="badge badge-ai">✨ טיוטה שהוצעה על ידי AI - בדקו לפני שמירה</span> }
    </div>

    @if (loading()) {
      <div class="empty"><span class="spinner"></span> טוען...</div>
    } @else {
      <!-- 1. פרטים כלליים -->
      <section class="card">
        <h2 class="card-title">1. פרטי הבקרה</h2>
        <div class="grid grid-2">
          <label class="field">שם הבקרה *
            <input [(ngModel)]="name" maxlength="150" placeholder="לדוגמה: קטינים עם שכר גבוה" />
          </label>
          <div class="grid grid-2">
            <label class="field">קטגוריה
              <input [(ngModel)]="category" list="categories" maxlength="50" />
              <datalist id="categories">@for (c of categories; track c) { <option [value]="c"></option> }</datalist>
            </label>
            <label class="field">חומרה
              <select [(ngModel)]="severity">
                <option value="high">גבוהה</option>
                <option value="medium">בינונית</option>
                <option value="low">נמוכה</option>
              </select>
            </label>
          </div>
        </div>
        <label class="field desc">תיאור
          <textarea [(ngModel)]="description" rows="2" maxlength="1000" placeholder="מה הבקרה בודקת ולמה זה חשוב"></textarea>
        </label>
      </section>

      <!-- 2. בחירת הנתונים -->
      <section class="card">
        <h2 class="card-title">2. על אילו נתונים תתבצע הבקרה?</h2>
        <div class="entities">
          @for (e of metadata.entities(); track e.key) {
            <button type="button" class="entity" [class.selected]="definition().rootEntity === e.key" (click)="chooseEntity(e.key)">
              <strong>{{ e.displayNamePlural }}</strong>
              <span class="small muted">{{ e.description }}</span>
            </button>
          }
        </div>
      </section>

      <!-- 3. תנאים -->
      @if (definition().rootEntity) {
        <section class="card">
          <div class="card-title">
            <h2>3. מתי רשומה נחשבת חריגה?</h2>
            <span class="small muted">* = שדה חובה לפי כללים עסקיים</span>
          </div>
          <app-condition-group [group]="definition().filter" [entityKey]="definition().rootEntity"
                               [errorIds]="errorIds()" (changed)="onChanged()" />
        </section>

        <!-- 4. סיכום ובדיקה -->
        <section class="card">
          <div class="card-title">
            <h2>4. סיכום</h2>
            @if (validating()) { <span class="small muted"><span class="spinner"></span> בודק...</span> }
          </div>
          @if (validation(); as v) {
            @if (v.isValid) {
              <div class="alert alert-success"><strong>הבקרה תאתר:</strong> {{ v.summary }}</div>
              @if (v.generatedSql) {
                <details class="sql">
                  <summary>השאילתה שתיווצר (לאנשי טכנולוגיה)</summary>
                  <pre class="sql">{{ v.generatedSql }}</pre>
                  @if (v.parameters?.length) {
                    <div class="small muted">פרמטרים: @for (p of v.parameters; track p.name) { <code>{{ p.name }}={{ p.value }}</code>&nbsp; }</div>
                  }
                </details>
              }
            } @else {
              <div class="alert alert-warning">
                <strong>יש להשלים את ההגדרה:</strong>
                <ul>@for (e of v.errors; track $index) { <li>{{ e.message }}</li> }</ul>
              </div>
            }
          }
          @if (saveErrors().length) {
            <div class="alert alert-error"><ul>@for (e of saveErrors(); track $index) { <li>{{ e }}</li> }</ul></div>
          }
        </section>
      }

      <div class="actions row">
        <a class="btn" routerLink="/controls">ביטול</a>
        <span class="spacer"></span>
        <button class="btn" (click)="testRun()" [disabled]="!canRun() || busy()">🧪 הרצת ניסיון</button>
        <button class="btn" (click)="save(false)" [disabled]="!canSave() || busy()">💾 שמירה</button>
        <button class="btn btn-primary" (click)="save(true)" [disabled]="!canSave() || busy()">💾 שמירה והפעלה ▶</button>
      </div>

      @if (preview(); as p) {
        <section class="card preview">
          <div class="card-title">
            <h2>תוצאות הרצת ניסיון</h2>
            <span class="badge" [class.badge-high]="p.exceptionCount" [class.badge-ok]="!p.exceptionCount">
              {{ p.exceptionCount }} חריגות מתוך {{ p.totalRecords }} {{ p.entityName }}
            </span>
          </div>
          @if (p.rows.length) {
            <app-results-table [columns]="p.columns" [rows]="p.rows" fileName="test-run" />
          } @else {
            <div class="empty"><span class="icon">✅</span>לא נמצאו חריגות</div>
          }
        </section>
      }
    }
  `,
  styles: `
    .desc { margin-top: 14px; }
    .entities { display: grid; gap: 10px; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); }
    .entity { display: flex; flex-direction: column; gap: 4px; align-items: flex-start; text-align: right; padding: 12px 14px;
      border: 1px solid var(--border); border-radius: 10px; background: var(--surface); cursor: pointer; font: inherit; }
    .entity:hover { border-color: var(--primary); }
    .entity.selected { border-color: var(--primary); background: var(--primary-soft); box-shadow: 0 0 0 1px var(--primary) inset; }
    .sql { margin-top: 12px; }
    .actions { position: sticky; bottom: 0; background: var(--bg); padding: 12px 0; margin-top: 16px; border-top: 1px solid var(--border); }
    .preview { margin-top: 16px; }
  `,
})
export class ControlEditorComponent implements OnInit, OnDestroy {
  readonly metadata = inject(MetadataStore);
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly drafts = inject(DraftTransferService);

  /** מגיע מהנתיב controls/:id/edit (withComponentInputBinding) */
  readonly id = input<string>();
  readonly isEdit = computed(() => !!this.id());
  readonly categories = CATEGORY_SUGGESTIONS;

  name = '';
  description = '';
  category = '';
  severity: Severity = 'medium';
  private source: 'manual' | 'ai' = 'manual';

  readonly definition = signal<ControlDefinition>(emptyDefinition());
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly fromAi = signal(false);
  readonly validation = signal<ValidateResponse | null>(null);
  readonly validating = signal(false);
  readonly saveErrors = signal<string[]>([]);
  readonly preview = signal<RunResult | null>(null);

  readonly errorIds = computed(() => new Set((this.validation()?.errors ?? []).map(e => e.nodeId).filter((x): x is string => !!x)));
  readonly canRun = computed(() => !!this.validation()?.isValid);

  private validateTimer?: ReturnType<typeof setTimeout>;
  private validateSub?: Subscription;

  canSave(): boolean {
    return this.canRun() && this.name.trim().length > 0;
  }

  async ngOnInit() {
    try {
      await this.metadata.load();
      const id = this.id();
      if (id) {
        const c = await firstValueFrom(this.api.getControl(+id));
        this.apply({ name: c.name, description: c.description, category: c.category, severity: c.severity, source: c.source, definition: c.definition });
      } else {
        const draft = this.drafts.take();
        if (draft) {
          this.apply(draft);
          this.fromAi.set(draft.source === 'ai');
        }
      }
    } catch (err) {
      this.notify.error(err, 'טעינת הבקרה נכשלה');
    } finally {
      this.loading.set(false);
    }
  }

  ngOnDestroy() {
    clearTimeout(this.validateTimer);
    this.validateSub?.unsubscribe();
  }

  chooseEntity(key: string) {
    const current = this.definition();
    if (current.rootEntity === key) return;
    const hasConditions = current.rootEntity && current.filter.children.some(c => c.type !== 'condition' || (c as { field: string }).field);
    if (hasConditions && !confirm('החלפת סוג הנתונים תמחק את התנאים שהוגדרו. להמשיך?')) return;
    this.definition.set(emptyDefinition(key));
    this.preview.set(null);
    this.validation.set(null);
  }

  onChanged() {
    this.preview.set(null);
    this.saveErrors.set([]);
    this.scheduleValidation();
  }

  private apply(d: SaveControlRequest) {
    this.name = d.name;
    this.description = d.description ?? '';
    this.category = d.category ?? '';
    this.severity = d.severity;
    this.source = d.source ?? 'manual';
    this.definition.set(cloneDefinition(d.definition));
    this.scheduleValidation(0);
  }

  private scheduleValidation(delay = 450) {
    clearTimeout(this.validateTimer);
    this.validateTimer = setTimeout(() => {
      this.validateSub?.unsubscribe();
      this.validating.set(true);
      this.validateSub = this.api.validate(this.definition()).subscribe({
        next: v => {
          this.validation.set(v);
          this.validating.set(false);
          // השרת משלים מזהי צמתים חסרים - מסנכרנים כדי שסימון השגיאות יתאים
          if (v.normalizedDefinition) this.syncIds(this.definition(), v.normalizedDefinition);
        },
        error: err => {
          this.validating.set(false);
          const e = describeError(err, 'בדיקת ההגדרה נכשלה');
          this.validation.set({ isValid: false, errors: e.errors.length ? e.errors : [{ message: e.message }] });
        },
      });
    }, delay);
  }

  private syncIds(target: ControlDefinition, normalized: ControlDefinition) {
    const walk = (a: any, b: any) => {
      if (!a || !b) return;
      a.id = b.id;
      a.children?.forEach((c: unknown, i: number) => walk(c, b.children?.[i]));
      if (a.filter) walk(a.filter, b.filter);
    };
    walk(target.filter, normalized.filter);
  }

  testRun() {
    this.busy.set(true);
    this.api.runPreview(this.definition()).subscribe({
      next: r => { this.preview.set(r); this.busy.set(false); },
      error: err => { this.busy.set(false); this.notify.error(err, 'הרצת הניסיון נכשלה'); },
    });
  }

  save(andRun: boolean) {
    const request: SaveControlRequest = {
      name: this.name.trim(),
      description: this.description.trim() || undefined,
      category: this.category.trim() || undefined,
      severity: this.severity,
      source: this.source,
      definition: this.definition(),
    };
    const id = this.id();
    this.busy.set(true);
    this.saveErrors.set([]);
    (id ? this.api.updateControl(+id, request) : this.api.createControl(request)).subscribe({
      next: saved => {
        this.busy.set(false);
        this.notify.success(id ? 'הבקרה עודכנה' : 'הבקרה נשמרה');
        this.router.navigate(andRun ? ['/controls', saved.id, 'run'] : ['/controls']);
      },
      error: err => {
        this.busy.set(false);
        const e = describeError(err, 'השמירה נכשלה');
        this.saveErrors.set([e.message, ...e.details]);
      },
    });
  }
}
