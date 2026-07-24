using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Preflight;
using InfraAgent.Core.Provisioning;
using InfraAgent.Core.Validation;
using InfraAgent.Tools.Git;
using InfraAgent.Tools.Processes;
using InfraAgent.Tools.Security;
using InfraAgent.Tools.Terraform;
using Microsoft.AspNetCore.Diagnostics;
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
builder.Services.AddSingleton<IS3BucketAvailabilityChecker, S3BucketAvailabilityChecker>();
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

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("InfraAgent.Api.GlobalExceptionHandler");

        if (exception is not null)
        {
            logger.LogError(exception, "Unhandled API exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        var statusCode = exception is null
            ? StatusCodes.Status500InternalServerError
            : GenerateHttpStatusMapper.ForException(exception);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(GenerateResponse.Failure(
            statusCode == StatusCodes.Status500InternalServerError
                ? "Unexpected API error. The request was not completed. Check backend logs for the detailed exception."
                : "The request failed before completion. Review the HTTP status and backend logs for details."));
    });
});

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
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.Json(
            GenerateResponse.Clarification("Describe the AWS S3 or EC2 infrastructure to generate, including a valid AWS region."),
            statusCode: StatusCodes.Status400BadRequest);
    }

    var response = await agent.GenerateAsync(request.Prompt, cancellationToken);
    return Results.Json(response, statusCode: GenerateHttpStatusMapper.ForResponse(response));
})
    .WithName("GenerateInfrastructure")
    .WithTags("Generation")
    .Produces<GenerateResponse>(StatusCodes.Status200OK)
    .Produces<GenerateResponse>(StatusCodes.Status400BadRequest)
    .Produces<GenerateResponse>(StatusCodes.Status401Unauthorized)
    .Produces<GenerateResponse>(StatusCodes.Status403Forbidden)
    .Produces<GenerateResponse>(StatusCodes.Status404NotFound)
    .Produces<GenerateResponse>(StatusCodes.Status409Conflict)
    .Produces<GenerateResponse>(StatusCodes.Status422UnprocessableEntity)
    .Produces<GenerateResponse>(StatusCodes.Status429TooManyRequests)
    .Produces<GenerateResponse>(StatusCodes.Status500InternalServerError)
    .Produces<GenerateResponse>(StatusCodes.Status502BadGateway)
    .Produces<GenerateResponse>(StatusCodes.Status503ServiceUnavailable)
    .Produces<GenerateResponse>(StatusCodes.Status504GatewayTimeout);

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
