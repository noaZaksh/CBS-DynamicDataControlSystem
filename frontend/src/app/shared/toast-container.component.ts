import { Component, inject } from '@angular/core';
import { NotificationService } from '../core/notification.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  template: `
    <div class="toasts" aria-live="polite">
      @for (t of notify.toasts(); track t.id) {
        <div class="toast alert" [class.alert-success]="t.kind === 'success'" [class.alert-error]="t.kind === 'error'"
             [class.alert-info]="t.kind === 'info'" role="status">
          <div class="row">
            <strong class="spacer">{{ t.text }}</strong>
            <button class="icon-btn" (click)="notify.dismiss(t.id)" aria-label="סגירה">✕</button>
          </div>
          @if (t.details?.length) {
            <ul>@for (d of t.details; track $index) { <li>{{ d }}</li> }</ul>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .toasts { position: fixed; bottom: 20px; left: 20px; z-index: 100; display: flex; flex-direction: column; gap: 8px; max-width: min(420px, calc(100vw - 40px)); }
    .toast { box-shadow: 0 6px 20px rgba(0,0,0,.12); background-clip: padding-box; }
  `,
})
export class ToastContainerComponent {
  readonly notify = inject(NotificationService);
}
