import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { ApiProblem, ValidationError } from './models';

export interface Toast {
  id: number;
  kind: 'success' | 'error' | 'info';
  text: string;
  details?: string[];
}

/** הודעות קופצות + המרת שגיאות HTTP להודעות ידידותיות בעברית. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  success(text: string) { this.push({ kind: 'success', text }); }
  info(text: string) { this.push({ kind: 'info', text }); }

  error(error: unknown, fallback = 'הפעולה נכשלה') {
    const { message, details } = describeError(error, fallback);
    this.push({ kind: 'error', text: message, details }, 8000);
  }

  dismiss(id: number) {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private push(toast: Omit<Toast, 'id'>, timeout = 4000) {
    const id = this.nextId++;
    this.toasts.update(list => [...list, { ...toast, id }]);
    setTimeout(() => this.dismiss(id), timeout);
  }
}

export function describeError(error: unknown, fallback = 'הפעולה נכשלה'): { message: string; details: string[]; errors: ValidationError[] } {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return { message: 'אין תקשורת עם השרת. ודאו שה-API פועל.', details: [], errors: [] };
    }
    const problem = error.error as ApiProblem | null;
    const errors = problem?.errors ?? [];
    return {
      message: problem?.title ?? fallback,
      details: errors.map(e => e.message),
      errors,
    };
  }
  return { message: fallback, details: [], errors: [] };
}
