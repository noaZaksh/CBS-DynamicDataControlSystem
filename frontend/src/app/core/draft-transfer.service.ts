import { Injectable } from '@angular/core';
import { SaveControlRequest } from './models';

/**
 * העברת טיוטת בקרה (למשל הצעת AI) למסך העורך, לצורך בדיקה ועריכה לפני שמירה.
 * הטיוטה נצרכת פעם אחת.
 */
@Injectable({ providedIn: 'root' })
export class DraftTransferService {
  private pending: SaveControlRequest | null = null;

  set(draft: SaveControlRequest) { this.pending = draft; }

  take(): SaveControlRequest | null {
    const d = this.pending;
    this.pending = null;
    return d;
  }
}
