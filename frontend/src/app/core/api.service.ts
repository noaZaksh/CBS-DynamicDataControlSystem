import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AiDraft, Control, ControlDefinition, ControlRunSummary, DataModel,
  RunResult, SaveControlRequest, ValidateResponse,
} from './models';

/** גישה ל-API. כל הקריאות עוברות דרך כאן - הקומפוננטות לא מכירות כתובות. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api`;

  // ---- metadata
  getDataModel(): Observable<DataModel> {
    return this.http.get<DataModel>(`${this.base}/metadata/model`);
  }

  // ---- controls
  listControls(search?: string, category?: string): Observable<Control[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (category) params = params.set('category', category);
    return this.http.get<Control[]>(`${this.base}/controls`, { params });
  }

  getControl(id: number): Observable<Control> {
    return this.http.get<Control>(`${this.base}/controls/${id}`);
  }

  createControl(request: SaveControlRequest): Observable<Control> {
    return this.http.post<Control>(`${this.base}/controls`, request);
  }

  updateControl(id: number, request: SaveControlRequest): Observable<Control> {
    return this.http.put<Control>(`${this.base}/controls/${id}`, request);
  }

  deleteControl(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/controls/${id}`);
  }

  duplicateControl(id: number): Observable<Control> {
    return this.http.post<Control>(`${this.base}/controls/${id}/duplicate`, {});
  }

  validate(definition: ControlDefinition): Observable<ValidateResponse> {
    return this.http.post<ValidateResponse>(`${this.base}/controls/validate`, { definition });
  }

  runControl(id: number): Observable<RunResult> {
    return this.http.post<RunResult>(`${this.base}/controls/${id}/run`, {});
  }

  runPreview(definition: ControlDefinition): Observable<RunResult> {
    return this.http.post<RunResult>(`${this.base}/controls/run-preview`, { definition });
  }

  getRuns(id: number, take = 10): Observable<ControlRunSummary[]> {
    return this.http.get<ControlRunSummary[]>(`${this.base}/controls/${id}/runs`, { params: { take } });
  }

  // ---- AI
  interpret(text: string): Observable<AiDraft> {
    return this.http.post<AiDraft>(`${this.base}/ai/interpret`, { text });
  }

  aiSuggestions(max = 10): Observable<AiDraft[]> {
    return this.http.get<AiDraft[]>(`${this.base}/ai/suggestions`, { params: { max } });
  }
}
