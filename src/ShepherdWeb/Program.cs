using ShepherdWeb.Components;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.Drafts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ShepherdAPI is the only source of domain data (docs/ARCHITECTURE.md §4, AD-2).
builder.Services.AddShepherdApi(builder.Configuration);

builder.Services.AddOptions<ConventionsOptions>()
    .Bind(builder.Configuration.GetSection(ConventionsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Scoped = per circuit (per browser tab) in Interactive Server (AD-4).
builder.Services.AddScoped<ReferenceData>();
builder.Services.AddScoped<VersionConfigurationLoader>();
builder.Services.AddScoped<IDraftStore, BrowserDraftStore>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposed for WebApplicationFactory<Program> in tests.
public partial class Program;
