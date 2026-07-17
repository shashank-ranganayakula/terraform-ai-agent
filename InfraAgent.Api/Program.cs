using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Validation;
using InfraAgent.Tools.Git;
using InfraAgent.Tools.Processes;
using InfraAgent.Tools.Security;
using InfraAgent.Tools.Terraform;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<GitOptions>(builder.Configuration.GetSection("Git"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IIntentParser, IntentParser>();
builder.Services.AddSingleton<IContextRetriever, FileContextRetriever>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ITerraformRunner, TerraformRunner>();
builder.Services.AddSingleton<ITflintRunner, TflintRunner>();
builder.Services.AddSingleton<DeterministicSecurityPolicy>();
builder.Services.AddSingleton<ISecurityScanner, TfsecSecurityScanner>();
builder.Services.AddSingleton<IInfrastructureValidator, InfrastructureValidator>();
builder.Services.AddSingleton<IInfrastructureAgent, InfrastructureAgent>();

builder.Services.AddSingleton<ITerraformGenerator>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var apiKey = configuration["OpenAI:ApiKey"];
    return string.IsNullOrWhiteSpace(apiKey)
        ? new TemplateTerraformGenerator()
        : ActivatorUtilities.CreateInstance<OpenAiTerraformGenerator>(services);
});

builder.Services.AddSingleton<IGitRepository>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var token = configuration["Git:GitHubToken"];
    return string.IsNullOrWhiteSpace(token)
        ? ActivatorUtilities.CreateInstance<LocalGitRepository>(services)
        : ActivatorUtilities.CreateInstance<GitHubRepository>(services);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { service = "InfraAgent.Api", status = "running" }))
    .WithName("HealthCheck")
    .WithTags("Status")
    .Produces(StatusCodes.Status200OK);

app.MapPost("/generate", async (
    GenerateRequest request,
    IInfrastructureAgent agent,
    CancellationToken cancellationToken) =>
{
    var response = await agent.GenerateAsync(request.Prompt, cancellationToken);
    return response.Status switch
    {
        "clarification_required" => Results.Ok(response),
        "succeeded" => Results.Ok(response),
        _ => Results.Problem(response.Error, statusCode: StatusCodes.Status422UnprocessableEntity)
    };
})
    .WithName("GenerateInfrastructure")
    .WithTags("Generation")
    .Produces<GenerateResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.Run();
