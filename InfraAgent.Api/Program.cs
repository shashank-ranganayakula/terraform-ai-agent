using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Provisioning;
using InfraAgent.Core.Validation;
using InfraAgent.Tools.Git;
using InfraAgent.Tools.Processes;
using InfraAgent.Tools.Security;
using InfraAgent.Tools.Terraform;
using Microsoft.Extensions.FileProviders;


var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));
builder.Services.Configure<GitOptions>(builder.Configuration.GetSection("Git"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IIntentParser, IntentParser>();
builder.Services.AddSingleton<IContextRetriever, RagContextRetriever>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ITerraformRunner, TerraformRunner>();
builder.Services.AddSingleton<ITflintRunner, TflintRunner>();
builder.Services.AddSingleton<DeterministicSecurityPolicy>();
builder.Services.AddSingleton<ISecurityScanner, TfsecSecurityScanner>();
builder.Services.AddSingleton<IInfrastructureValidator, InfrastructureValidator>();
builder.Services.AddSingleton<IInfrastructureProvisioner, TerraformProvisioner>();
builder.Services.AddSingleton<IInfrastructureAgent, InfrastructureAgent>();

builder.Services.AddSingleton<ITerraformGenerator>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return ActivatorUtilities.CreateInstance<OpenAiTerraformGenerator>(services);
});

builder.Services.AddSingleton<IGitRepository>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var token = configuration["Git:GitHubToken"];
    return string.IsNullOrWhiteSpace(token)
        ? ActivatorUtilities.CreateInstance<LocalGitRepository>(services)
        : ActivatorUtilities.CreateInstance<GitHubRepository>(services);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AngularPolicy");
app.UseDefaultFiles();
app.UseStaticFiles();
var generatedPath = Path.Combine(app.Environment.ContentRootPath, "generated-repositories");

var testFile = Path.Combine(
    generatedPath,
    "infra-agent-20260718164038",
    "README.md");

Console.WriteLine($"Generated Path: {generatedPath}");
Console.WriteLine($"Directory Exists: {Directory.Exists(generatedPath)}");
Console.WriteLine($"Test File: {testFile}");
Console.WriteLine($"File Exists: {File.Exists(testFile)}");

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(generatedPath),
    RequestPath = "/generated-repositories"
});


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
        _ => Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity)
    };
})
    .WithName("GenerateInfrastructure")
    .WithTags("Generation")
    .Produces<GenerateResponse>(StatusCodes.Status200OK)
    .Produces<GenerateResponse>(StatusCodes.Status422UnprocessableEntity);

app.MapGet("/debug/repos", () =>
{
    var root = Path.Combine(app.Environment.ContentRootPath, "generated-repositories");

    return Directory.GetDirectories(root)
        .Select(Path.GetFileName)
        .ToList();
});

app.MapGet("/debug/readme", () =>
{
    var path = Path.Combine(
        app.Environment.ContentRootPath,
        "generated-repositories",
        "infra-agent-20260718164038",
        "README.md");

    return Results.File(path, "text/plain");
});

app.Run();
