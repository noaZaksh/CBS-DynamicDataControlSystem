import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ToastContainerComponent } from './shared/toast-container.component';
import { MetadataStore } from './core/metadata.store';
import { NotificationService } from './core/notification.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastContainerComponent],
  template: `
    <header class="topbar">
      <a routerLink="/controls" class="brand">
        <span class="brand-mark">✓</span>
        <span>מערכת בקרות נתונים</span>
      </a>
      <nav class="nav">
        <a routerLink="/controls" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">בקרות</a>
        <a routerLink="/controls/new" routerLinkActive="active">בקרה חדשה</a>
        <a routerLink="/ai" routerLinkActive="active">✨ בשפה חופשית</a>
        <a routerLink="/dictionary" routerLinkActive="active">מילון נתונים</a>
      </nav>
    </header>
    <main class="page">
      <router-outlet />
    </main>
    <app-toast-container />
  `,
})
export class AppComponent {
  constructor() {
    // טעינה מוקדמת של מילון הנתונים - כל המסכים נשענים עליו
    const metadata = inject(MetadataStore);
    const notify = inject(NotificationService);
    metadata.load().catch(err => notify.error(err, 'טעינת מילון הנתונים נכשלה'));
  }
}
