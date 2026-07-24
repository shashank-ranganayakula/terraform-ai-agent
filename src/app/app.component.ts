import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, GenerateResponse } from './api.service';

interface Message {
  role: 'user' | 'assistant';
  text: string;
  statusCode?: number;
  repositoryUrl?: string;
  repositoryLink?: string;
  filesCreated?: string[];
  assumptions?: string[];
  provisioningStatus?: string;
  provisioningOutput?: string;
  applySummary?: TerraformApplySummary;
  errorExplanation?: ErrorExplanation;
  showRawLogs?: boolean;
}

interface TerraformApplySummary {
  success: boolean;
  resourcesCreated?: number;
  resourcesModified?: number;
  resourcesDeleted?: number;
  bucketName?: string;
  awsRegion?: string;
  hasS3Bucket: boolean;
  hasPublicAccessBlock: boolean;
  hasEncryption: boolean;
  outputs: TerraformOutput[];
  resourcesCreatedList: string[];
  timeline: string[];
}

interface TerraformOutput {
  name: string;
  value: string;
}

interface ErrorExplanation {
  title: string;
  message: string;
  detail?: string;
  statusCode?: number;
  nextSteps: string[];
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
        const statusCode = typeof error.status === 'number' ? error.status : undefined;
        const message = response?.error ?? response?.clarifyingQuestion ?? error.error?.detail ?? error.error?.title ?? 'The API is unavailable.';
        this.messages.push({
          role: 'assistant',
          text: message,
          statusCode,
          provisioningStatus: response?.provisioningStatus,
          provisioningOutput: response?.provisioningOutput,
          errorExplanation: this.explainError(message, response?.provisioningOutput, statusCode)
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

  toggleRawLogs(message: Message): void {
    message.showRawLogs = !message.showRawLogs;
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
        provisioningOutput: response.provisioningOutput,
        applySummary: this.parseTerraformApply(response.provisioningOutput)
      };
    }

    return {
      role: 'assistant',
      text: response.error ?? response.clarifyingQuestion ?? 'Please clarify your request.',
      provisioningStatus: response.provisioningStatus,
      provisioningOutput: response.provisioningOutput,
      errorExplanation: response.error
        ? this.explainError(response.error, response.provisioningOutput)
        : undefined
    };
  }

  private parseTerraformApply(output?: string): TerraformApplySummary | undefined {
    if (!output) {
      return undefined;
    }

    const applyMatch = output.match(/Apply complete!\s*Resources:\s*(\d+)\s*added,\s*(\d+)\s*changed,\s*(\d+)\s*destroyed\./i);
    if (!applyMatch) {
      return undefined;
    }

    const hasS3Bucket = /aws_s3_bucket\.[\w-]+(?:\[[^\]]+\])?:\s+Creation complete/i.test(output);
    const hasPublicAccessBlock = /aws_s3_bucket_public_access_block\.[\w-]+(?:\[[^\]]+\])?:\s+Creation complete/i.test(output);
    const hasEncryption = /aws_s3_bucket_server_side_encryption_configuration\.[\w-]+(?:\[[^\]]+\])?:\s+Creation complete/i.test(output);
    const outputs = this.parseTerraformOutputs(output);
    const bucketOutput = outputs.find(item => item.name === 'bucket_name');
    const bucketName = bucketOutput?.value ?? this.matchFirst(output, /bucket\s*=\s*"([^"]+)"/i);
    const awsRegion = this.matchFirst(output, /(?:aws_region|region)\s*=\s*"([^"]+)"/i);
    const resourcesCreatedList = [
      hasS3Bucket && bucketName ? `Amazon S3 Bucket (${bucketName})` : hasS3Bucket ? 'Amazon S3 Bucket' : undefined,
      hasPublicAccessBlock ? 'S3 Public Access Block Configuration' : undefined,
      hasEncryption ? 'S3 Server-side Encryption Configuration' : undefined
    ].filter((item): item is string => Boolean(item));
    const timeline = [
      'Generated execution plan',
      hasS3Bucket ? 'Created Amazon S3 bucket' : undefined,
      hasPublicAccessBlock ? 'Applied public access restrictions' : undefined,
      hasEncryption ? 'Enabled server-side encryption' : undefined,
      'Verified infrastructure',
      'Deployment completed successfully'
    ].filter((item): item is string => Boolean(item));

    return {
      success: true,
      resourcesCreated: Number(applyMatch[1]),
      resourcesModified: Number(applyMatch[2]),
      resourcesDeleted: Number(applyMatch[3]),
      bucketName,
      awsRegion,
      hasS3Bucket,
      hasPublicAccessBlock,
      hasEncryption,
      outputs,
      resourcesCreatedList,
      timeline
    };
  }

  private parseTerraformOutputs(output: string): TerraformOutput[] {
    const outputsStart = output.search(/^Outputs:/im);
    if (outputsStart < 0) {
      return [];
    }

    return output
      .slice(outputsStart)
      .split(/\r?\n/)
      .map(line => line.match(/^\s*([A-Za-z_][\w-]*)\s*=\s*"?([^"\r\n]+)"?\s*$/))
      .filter((match): match is RegExpMatchArray => Boolean(match))
      .map(match => ({ name: match[1], value: match[2].trim() }));
  }

  private matchFirst(output: string, pattern: RegExp): string | undefined {
    return output.match(pattern)?.[1];
  }

  private explainError(message: string, output?: string, statusCode?: number): ErrorExplanation {
    const details = [message, output].filter(Boolean).join('\n').toLowerCase();
    const clippedDetail = output ? this.clip(output, 900) : undefined;

    const statusExplanation = this.explainStatusCode(statusCode, clippedDetail);
    if (statusExplanation) {
      return statusExplanation;
    }

    if (details.includes('which aws region') || details.includes('aws region is required')) {
      return {
        title: 'AWS region required',
        message: 'InfraAgent needs an explicit AWS region before it can generate infrastructure.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Add a valid region code such as ap-south-2 or us-east-1 to the prompt.',
          'Resubmit the same infrastructure request with the region included.'
        ]
      };
    }

    if (details.includes('supported aws region list') || details.includes('invalid aws region')) {
      return {
        title: 'Invalid AWS region',
        message: 'The region in the prompt is not recognized as a valid AWS region.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Check the region code spelling, for example ap-south-2.',
          'Use a region that is enabled for your AWS account.'
        ]
      };
    }

    if (details.includes('github') || details.includes('repository publishing failed')) {
      return {
        title: 'GitHub publishing failed',
        message: 'Terraform was generated, but InfraAgent could not publish the repository to GitHub.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Confirm the GitHub owner in appsettings.json matches the token permissions.',
          'Check that the token can create repositories for that owner.'
        ]
      };
    }

    if (details.includes('terraform is required') || details.includes('terraform') && details.includes('path')) {
      return {
        title: 'Terraform CLI unavailable',
        message: 'The backend could not run Terraform from the server environment.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Install Terraform on the backend machine.',
          'Restart the backend after Terraform is available on PATH.'
        ]
      };
    }

    if (details.includes('accessdenied') || details.includes('invalidclienttokenid') || details.includes('credential')) {
      return {
        title: 'AWS authentication or permission issue',
        message: 'Terraform reached AWS but the configured credentials were rejected or lack permission.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Verify the AWS credentials used by the backend.',
          'Ensure the IAM principal can create and configure the requested resources.'
        ]
      };
    }

    if (details.includes('s3_unencrypted') || details.includes('server-side encryption')) {
      return {
        title: 'Security validation failed',
        message: 'The generated Terraform did not satisfy the project security guardrails.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Retry the request so InfraAgent can repair the Terraform.',
          'Include encryption and public access requirements explicitly in the prompt.'
        ]
      };
    }

    if (details.includes('terraform apply failed')) {
      return {
        title: 'Terraform apply failed',
        message: 'Terraform validation passed, but AWS provisioning did not complete.',
        detail: clippedDetail,
        statusCode,
        nextSteps: [
          'Review the raw Terraform logs below for the provider error.',
          'Fix the prompt or AWS account prerequisite reported by Terraform, then retry.'
        ]
      };
    }

    return {
      title: 'Request failed',
      message: 'InfraAgent could not complete the request. The backend returned the details below.',
      detail: clippedDetail,
      statusCode,
      nextSteps: [
        'Review the error detail and raw logs if present.',
        'Retry with a more specific prompt if the request was ambiguous.'
      ]
    };
  }

  private clip(value: string, maxLength: number): string {
    return value.length <= maxLength ? value : `${value.slice(0, maxLength)}...`;
  }

  private explainStatusCode(statusCode: number | undefined, detail?: string): ErrorExplanation | undefined {
    switch (statusCode) {
      case 400:
        return {
          title: 'Invalid request or prompt',
          message: 'InfraAgent needs a more complete or safer infrastructure request before it can generate Terraform.',
          detail,
          statusCode,
          nextSteps: [
            'Include the AWS service, resource name, and a valid AWS region.',
            'Remove unsupported services, credentials, destructive actions, or public S3 access.'
          ]
        };
      case 401:
        return {
          title: 'Authentication required',
          message: 'A required credential is missing or rejected by the API, GitHub, AWS, or the AI provider.',
          detail,
          statusCode,
          nextSteps: [
            'Verify configured API keys, GitHub token, and AWS credentials on the backend machine.',
            'Restart the backend after updating credentials.'
          ]
        };
      case 403:
        return {
          title: 'Permission denied',
          message: 'The configured credentials are valid, but they do not have permission for this operation.',
          detail,
          statusCode,
          nextSteps: [
            'Check IAM permissions for the requested AWS resources.',
            'Check GitHub token permissions if the failure happened while publishing.'
          ]
        };
      case 404:
        return {
          title: 'Resource not found',
          message: 'A required upstream resource was not found, such as a GitHub owner, repository target, or generated file.',
          detail,
          statusCode,
          nextSteps: [
            'Confirm the configured GitHub owner exists and is accessible by the token.',
            'Retry after verifying the backend configuration.'
          ]
        };
      case 409:
        return {
          title: 'Resource conflict',
          message: 'AWS or GitHub reported that the requested resource conflicts with an existing resource.',
          detail,
          statusCode,
          nextSteps: [
            'Use a globally unique S3 bucket name or a different repository name.',
            'Retry with a name that is not already taken.'
          ]
        };
      case 422:
        return {
          title: 'Terraform could not be processed',
          message: 'The AI output or request passed intake, but the generated Terraform could not be validated or processed.',
          detail,
          statusCode,
          nextSteps: [
            'Retry the request so InfraAgent can regenerate and repair the Terraform.',
            'Make the prompt more specific about S3 or EC2 requirements.'
          ]
        };
      case 429:
        return {
          title: 'Rate limit exceeded',
          message: 'An upstream provider is temporarily throttling requests.',
          detail,
          statusCode,
          nextSteps: [
            'Wait a short time before retrying.',
            'Reduce repeated submissions while a generation request is already running.'
          ]
        };
      case 500:
        return {
          title: 'Internal server error',
          message: 'The backend hit an unexpected error while processing the request.',
          detail,
          statusCode,
          nextSteps: [
            'Check the backend console logs for the exception.',
            'Retry after fixing the logged backend issue.'
          ]
        };
      case 502:
        return {
          title: 'Upstream service failure',
          message: 'GitHub, AWS, Terraform provider, or the AI provider failed while InfraAgent was processing the request.',
          detail,
          statusCode,
          nextSteps: [
            'Review the raw logs to identify which upstream service failed.',
            'Verify credentials, network access, and provider availability before retrying.'
          ]
        };
      case 503:
        return {
          title: 'Service unavailable',
          message: 'A required backend dependency is unavailable or not configured.',
          detail,
          statusCode,
          nextSteps: [
            'Confirm Terraform and required tooling are installed on the backend machine.',
            'Confirm backend configuration values are present, then restart the API.'
          ]
        };
      case 504:
        return {
          title: 'Deployment timeout',
          message: 'The deployment or an upstream call took too long to complete.',
          detail,
          statusCode,
          nextSteps: [
            'Wait a few minutes and check AWS or GitHub before retrying.',
            'Retry with the same prompt if no resources were created.'
          ]
        };
      default:
        return undefined;
    }
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
