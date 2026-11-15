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
  templateUrl: './control-editor.component.html',
  styleUrl: './control-editor.component.scss',
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
