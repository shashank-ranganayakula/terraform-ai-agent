using System.Text.Json;
using System.ClientModel;
using InfraAgent.Core.Context;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace InfraAgent.Core.Generation;

public sealed class OpenAiTerraformGenerator : ITerraformGenerator
{
    private readonly ChatClient _client;

    public OpenAiTerraformGenerator(IOptions<OpenAiOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.BaseUrl))
            throw new InvalidOperationException("OpenAI BaseUrl is required.");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Value.BaseUrl, UriKind.Absolute)
        };

        var apiKey = string.IsNullOrWhiteSpace(options.Value.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            : options.Value.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is missing. Set OpenAI:ApiKey in appsettings.json or the OPENAI_API_KEY environment variable. The configured service requires authentication.");
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _client = openAiClient.GetChatClient(options.Value.Model);
    }

    public async Task<GeneratedTerraform> GenerateAsync(
        InfrastructureIntent intent,
        IReadOnlyList<ContextDocument> context,
        string? repairInstructions,
        CancellationToken cancellationToken)
    {
        var tool = ChatTool.CreateFunctionTool(
            functionName: "emit_terraform",
            functionDescription: "Generate Terraform project.",
            functionParameters: BinaryData.FromString("""
{
    "type":"object",
    "properties":{
        "files":{
            "type":"array",
            "items":{
                "type":"object",
                "properties":{
                    "path":{"type":"string"},
                    "content":{"type":"string"}
                },
                "required":["path","content"]
            }
        },
        "summary":{
            "type":"string"
        },
        "assumptions":{
            "type":"array",
            "items":{"type":"string"}
        }
    },
    "required":["files","summary","assumptions"]
}
"""));

        var systemPrompt = """
You are a Senior AWS Cloud Architect.

Generate production-quality Terraform.

##############################
GENERAL RULES
##############################

Preserve every value specified by the user.

The Intent object is authoritative. Copy every value from it exactly into Terraform. In particular, copy each IngressRuleIntent.CidrBlock exactly; never rewrite, broaden, normalize, or replace a CIDR. An ingress CIDR of 10.0.0.0/8 must remain 10.0.0.0/8.

Do not invent infrastructure inputs that are absent from Intent. For EC2, do not add subnet_id, vpc_id, key_name, or user_data unless the user explicitly requested them and they exist in the intent. Never reference var.X unless variable "X" is declared in variables.tf and has a matching value or safe default. Before returning, verify every var.X reference across all .tf files has a declaration.

Never reference data.X.Y unless a matching data source block is declared in the same Terraform module. Do not add vpc_id, data.aws_vpc.default, subnet_id, or other VPC networking references for a basic EC2 request unless the user explicitly supplies the networking requirement. Before returning, verify every resource and data reference is declared.

Every declared Terraform variable must be used.

Do not declare unused variables in variables.tf or terraform.tfvars.

If Intent.Ec2Instance.IngressRules is empty, generate no ingress blocks at all. Never default an ingress rule to 0.0.0.0/0 or ::/0. If an ingress rule exists, copy its CIDR exactly and use only that CIDR.

If user specifies

- bucket name
- instance name
- AWS Region
- tags
- encryption
- versioning
- lifecycle

use exactly those values.

Never invent placeholder names.

##############################
SUPPORTED SERVICES
##############################

Only generate

- AWS S3
- AWS EC2

##############################
SECURITY RULES
##############################

Always

? Enable S3 Encryption

? Enable Block Public Access

? Enable Versioning if requested

? Use latest Amazon Linux 2023 AMI

? Use data.aws_ami

? Generate outputs

? Generate variables

? Generate provider block

? Generate README

Never

? Public S3

? IAM Wildcards

? 0.0.0.0/0

? ::/0

? Hardcoded credentials

##############################
FILES
##############################

Always generate

provider.tf

main.tf

variables.tf

outputs.tf

terraform.tfvars

README.md

##############################
OUTPUT
##############################

Return ONLY through emit_terraform().
""";

        var userPrompt = JsonSerializer.Serialize(new
        {
            UserRequest = intent.OriginalPrompt,
            Intent = intent,
            Context = context,
            RepairInstructions = repairInstructions
        });

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var chatOptions = new ChatCompletionOptions();

        chatOptions.Tools.Add(tool);

        ChatCompletion completion =
            await _client.CompleteChatAsync(
                messages,
                chatOptions,
                cancellationToken);

        var toolCall =
            completion.ToolCalls.FirstOrDefault(
                x => x.FunctionName == "emit_terraform");

        if (toolCall == null)
            throw new InvalidOperationException(
                "AI did not return Terraform.");

        using JsonDocument document =
            JsonDocument.Parse(toolCall.FunctionArguments.ToString());

        var files =
            document.RootElement
                .GetProperty("files")
                .EnumerateArray()
                .ToDictionary(
                    f => f.GetProperty("path").GetString()!,
                    f => f.GetProperty("content").GetString()!,
                    StringComparer.OrdinalIgnoreCase);

        string summary =
            document.RootElement
                .GetProperty("summary")
                .GetString()!;

        string[] assumptions =
            document.RootElement
                .GetProperty("assumptions")
                .EnumerateArray()
                .Select(x => x.GetString()!)
                .ToArray();

        return new GeneratedTerraform(
            files,
            summary,
            assumptions);
    }
}
