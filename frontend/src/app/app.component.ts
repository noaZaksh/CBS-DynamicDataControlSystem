import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ToastContainerComponent } from './shared/toast-container/toast-container.component';
import { MetadataStore } from './core/services/metadata.store';
import { NotificationService } from './core/services/notification.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastContainerComponent],
  templateUrl: './app.component.html',
})
export class AppComponent {
  constructor() {
    // טעינה מוקדמת של מילון הנתונים - כל המסכים נשענים עליו
    const metadata = inject(MetadataStore);
    const notify = inject(NotificationService);
    metadata.load().catch(err => notify.error(err, 'טעינת מילון הנתונים נכשלה'));
  }
}
