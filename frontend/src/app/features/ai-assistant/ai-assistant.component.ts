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
  templateUrl: './ai-assistant.component.html',
  styleUrl: './ai-assistant.component.scss',
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
