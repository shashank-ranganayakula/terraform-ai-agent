import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface GenerateResponse {
  status: string;
  clarifyingQuestion?: string;
  repositoryUrl?: string;
  filesCreated: string[];
  summary: string;
  assumptions: string[];
  error?: string;
  provisioningStatus?: string;
  provisioningOutput?: string;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  generate(prompt: string): Observable<GenerateResponse> {
    return this.http.post<GenerateResponse>(`${environment.apiUrl}/generate`, { prompt });
  }
}
