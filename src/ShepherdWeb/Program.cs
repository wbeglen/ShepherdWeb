using ShepherdWeb.Components;
using ShepherdWeb.Features.Artifacts;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Infrastructure.Preferences;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ShepherdAPI is the only source of domain data (docs/ARCHITECTURE.md §4, AD-2).
builder.Services.AddShepherdApi(builder.Configuration);

builder.Services.AddOptions<ConventionsOptions>()
    .Bind(builder.Configuration.GetSection(ConventionsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ArtifactStoreOptions>()
    .Bind(builder.Configuration.GetSection(ArtifactStoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The artifact store holds no per-user state, so one instance serves every circuit.
builder.Services.AddSingleton<ArtifactFileStore>();

// Scoped = per circuit (per browser tab) in Interactive Server (AD-4).
builder.Services.AddScoped<ReferenceData>();
builder.Services.AddScoped<VersionConfigurationLoader>();
builder.Services.AddScoped<IDraftStore, BrowserDraftStore>();
builder.Services.AddScoped<IPreferenceStore, BrowserPreferenceStore>();
builder.Services.AddScoped<ExecutionLoader>();
builder.Services.AddScoped<ArtifactRegistrar>();
builder.Services.AddScoped<StartExecutionWorkflow>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapArtifactFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposed for WebApplicationFactory<Program> in tests.
public partial class Program;
