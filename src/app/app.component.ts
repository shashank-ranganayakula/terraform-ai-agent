import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, GenerateResponse } from './api.service';

interface Message {
  role: 'user' | 'assistant';
  text: string;
  repositoryUrl?: string;
  repositoryLink?: string;
  filesCreated?: string[];
  assumptions?: string[];
  provisioningStatus?: string;
  provisioningOutput?: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private readonly api = inject(ApiService);

  prompt = '';
  loading = false;
  messages: Message[] = [];

  send(): void {
    const text = this.prompt.trim();
    if (!text || this.loading) {
      return;
    }

    this.messages.push({ role: 'user', text });
    this.prompt = '';
    this.loading = true;

    this.api.generate(text).subscribe({
      next: response => {
        this.messages.push(this.toMessage(response));
        this.loading = false;
      },
      error: error => {
        const response = error.error as Partial<GenerateResponse> | undefined;
        const message = response?.error ?? error.error?.detail ?? error.error?.title ?? 'The API is unavailable.';
        this.messages.push({
          role: 'assistant',
          text: message,
          provisioningStatus: response?.provisioningStatus,
          provisioningOutput: response?.provisioningOutput
        });
        this.loading = false;
      }
    });
  }

  keydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  private toMessage(response: GenerateResponse): Message {
    if (response.status === 'succeeded') {
      return {
        role: 'assistant',
        text: response.summary || 'Terraform generated successfully.',
        repositoryUrl: response.repositoryUrl,
        repositoryLink: this.repositoryLink(response.repositoryUrl),
        filesCreated: response.filesCreated ?? [],
        assumptions: response.assumptions ?? [],
        provisioningStatus: response.provisioningStatus,
        provisioningOutput: response.provisioningOutput
      };
    }

    return {
      role: 'assistant',
      text: response.error ?? response.clarifyingQuestion ?? 'Please clarify your request.'
    };
  }

  private repositoryLink(repositoryUrl?: string): string | undefined {
    if (!repositoryUrl) {
      return undefined;
    }

    const normalized = repositoryUrl.replaceAll('\\', '/');
    const marker = 'generated-repositories/';
    const markerIndex = normalized.toLowerCase().indexOf(marker);

    if (markerIndex >= 0) {
      return `/${normalized.slice(markerIndex)}/README.md`;
    }

    if (normalized.startsWith('http://') || normalized.startsWith('https://')) {
      return normalized;
    }

    return undefined;
  }
}
