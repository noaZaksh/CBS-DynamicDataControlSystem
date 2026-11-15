import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'controls' },
  {
    path: 'controls',
    title: 'בקרות',
    loadComponent: () => import('./features/controls-list/controls-list.component').then(m => m.ControlsListComponent),
  },
  {
    path: 'controls/new',
    title: 'בקרה חדשה',
    loadComponent: () => import('./features/control-editor/control-editor.component').then(m => m.ControlEditorComponent),
  },
  {
    path: 'controls/:id/edit',
    title: 'עריכת בקרה',
    loadComponent: () => import('./features/control-editor/control-editor.component').then(m => m.ControlEditorComponent),
  },
  {
    path: 'controls/:id/run',
    title: 'תוצאות בקרה',
    loadComponent: () => import('./features/control-run/control-run.component').then(m => m.ControlRunComponent),
  },
  {
    path: 'ai',
    title: 'הגדרת בקרה בשפה חופשית',
    loadComponent: () => import('./features/ai-assistant/ai-assistant.component').then(m => m.AiAssistantComponent),
  },
  {
    path: 'dictionary',
    title: 'מילון נתונים',
    loadComponent: () => import('./features/data-dictionary/data-dictionary.component').then(m => m.DataDictionaryComponent),
  },
  { path: '**', redirectTo: 'controls' },
];
