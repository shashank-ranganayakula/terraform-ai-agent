using System.Text.Json;
using InfraAgent.Core.Context;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace InfraAgent.Core.Generation;

public sealed class OpenAiTerraformGenerator : ITerraformGenerator
{
    private readonly ChatClient _client;

    public OpenAiTerraformGenerator(IOptions<OpenAiOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is required for OpenAI Terraform generation.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.Value.BaseUrl))
        {
            clientOptions.Endpoint = new Uri(options.Value.BaseUrl);
        }

        _client = new ChatClient(options.Value.Model, new ApiKeyCredential(options.Value.ApiKey), clientOptions);
    }

    public async Task<GeneratedTerraform> GenerateAsync(
        InfrastructureIntent intent,
        IReadOnlyList<ContextDocument> context,
        string? repairInstructions,
        CancellationToken cancellationToken)
    {
        var tool = ChatTool.CreateFunctionTool(
            functionName: "emit_terraform",
            functionDescription: "Emit Terraform files for the requested Phase 1 AWS infrastructure.",
            functionParameters: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "files": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "path": { "type": "string" },
                      "content": { "type": "string" }
                    },
                    "required": ["path", "content"]
                  }
                },
                "summary": { "type": "string" },
                "assumptions": {
                  "type": "array",
                  "items": { "type": "string" }
                }
              },
              "required": ["files", "summary", "assumptions"]
            }
            """));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
            You generate production Terraform for an AWS infrastructure provisioning agent.
            Phase 1 supports only aws_s3_bucket with versioning, encryption, and public access block resources,
            and aws_instance with aws_security_group and a data aws_ami lookup.
            Never create public S3 access, unencrypted S3 buckets, security group ingress from 0.0.0.0/0 or ::/0, IAM wildcard actions, remote backends, CI/CD, RDS, Lambda, multi-cloud, or VPC-from-scratch.
            Return the result by calling emit_terraform.
            """),
            new UserChatMessage(JsonSerializer.Serialize(new
            {
                intent,
                context,
                repairInstructions
            }))
        };

        var options = new ChatCompletionOptions();
        options.Tools.Add(tool);

        ChatCompletion completion = await _client.CompleteChatAsync(messages, options, cancellationToken);
        var toolCall = completion.ToolCalls.FirstOrDefault(call => call.FunctionName == "emit_terraform");
        if (toolCall is null)
        {
            throw new InvalidOperationException("The model did not call emit_terraform.");
        }

        using var document = JsonDocument.Parse(toolCall.FunctionArguments.ToString());
        var root = document.RootElement;
        var files = root.GetProperty("files")
            .EnumerateArray()
            .ToDictionary(
                file => file.GetProperty("path").GetString() ?? throw new InvalidOperationException("Generated file path is missing."),
                file => file.GetProperty("content").GetString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var summary = root.GetProperty("summary").GetString() ?? "Generated Terraform.";
        var assumptions = root.GetProperty("assumptions").EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();

        return new GeneratedTerraform(files, summary, assumptions);
    }
}
