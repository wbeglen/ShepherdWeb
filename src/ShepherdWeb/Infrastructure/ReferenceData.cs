using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Infrastructure;

/// <summary>
/// The six global lookup lists, loaded once per circuit (docs/ARCHITECTURE.md AD-4). Registered
/// as scoped, which in Interactive Server means "per browser tab", not "per HTTP request".
/// Reference-data pages call <see cref="RefreshAsync"/> after an edit.
/// </summary>
public sealed class ReferenceData(
    IAgentsClient agents,
    IArtifactTypesClient artifactTypes,
    IPhasesClient phases,
    IRatersClient raters,
    IScoreCategoriesClient scoreCategories,
    IStatusesClient statuses)
{
    private Task? _load;

    public IReadOnlyList<Agent> Agents { get; private set; } = [];
    public IReadOnlyList<ArtifactType> ArtifactTypes { get; private set; } = [];

    /// <summary>In phase Sequence order (ShepherdAPI returns them that way).</summary>
    public IReadOnlyList<Phase> Phases { get; private set; } = [];

    public IReadOnlyList<Rater> Raters { get; private set; } = [];
    public IReadOnlyList<ScoreCategory> ScoreCategories { get; private set; } = [];
    public IReadOnlyList<Status> Statuses { get; private set; } = [];

    public bool IsLoaded { get; private set; }

    /// <summary>The failure of the most recent load attempt, or null if it succeeded (or none finished yet).</summary>
    public Exception? LastLoadError { get; private set; }

    /// <summary>
    /// Raised after every load attempt, successful or not, so components showing ShepherdAPI state
    /// (ApiStatusIndicator) stay in step with a reload triggered anywhere else in the circuit.
    /// </summary>
    public event Action? LoadCompleted;

    /// <summary>
    /// Loads on first call; later calls reuse the same load. Concurrent callers in one circuit share
    /// one in-flight task. A failed load is forgotten so the next call retries; the exception
    /// propagates to the caller, which maps it with ApiErrorMapper.
    /// </summary>
    public Task EnsureLoadedAsync() => AwaitLoadAsync(_load ??= LoadAsync());

    public Task RefreshAsync() => AwaitLoadAsync(_load = LoadAsync());

    // The failed task is forgotten here, after it has been stored, rather than inside LoadAsync:
    // a load that fails before its first real await completes synchronously, so a reset inside
    // LoadAsync would run before "_load ??= ..." stored the faulted task, caching the failure forever.
    private async Task AwaitLoadAsync(Task load)
    {
        try
        {
            await load;
        }
        catch
        {
            if (ReferenceEquals(_load, load))
            {
                _load = null;
            }

            throw;
        }
    }

    // One call per load attempt, however many components await it, so LoadCompleted fires once.
    private async Task LoadAsync()
    {
        try
        {
            var agentsTask = agents.AgentsGetAsync();
            var artifactTypesTask = artifactTypes.ArtifactTypesGetAsync();
            var phasesTask = phases.PhasesGetAsync();
            var ratersTask = raters.RatersGetAsync();
            var scoreCategoriesTask = scoreCategories.ScoreCategoriesGetAsync();
            var statusesTask = statuses.StatusesGetAsync();
            await Task.WhenAll(agentsTask, artifactTypesTask, phasesTask, ratersTask, scoreCategoriesTask, statusesTask);

            Agents = agentsTask.Result;
            ArtifactTypes = artifactTypesTask.Result;
            Phases = phasesTask.Result;
            Raters = ratersTask.Result;
            ScoreCategories = scoreCategoriesTask.Result;
            Statuses = statusesTask.Result;
            IsLoaded = true;
            LastLoadError = null;
        }
        catch (Exception ex)
        {
            LastLoadError = ex;
            throw;
        }
        finally
        {
            LoadCompleted?.Invoke();
        }
    }

    public string AgentName(int id) => Agents.FirstOrDefault(a => a.AgentID == id)?.Name ?? Unknown("Agent", id);
    public string ArtifactTypeName(int id) => ArtifactTypes.FirstOrDefault(t => t.ArtifactTypeID == id)?.Name ?? Unknown("ArtifactType", id);
    public string PhaseName(int id) => Phases.FirstOrDefault(p => p.PhaseID == id)?.Name ?? Unknown("Phase", id);
    public string RaterName(int id) => Raters.FirstOrDefault(r => r.RaterID == id)?.Name ?? Unknown("Rater", id);
    public string ScoreCategoryName(int id) => ScoreCategories.FirstOrDefault(c => c.ScoreCategoryID == id)?.Name ?? Unknown("ScoreCategory", id);
    public string StatusName(int id) => Statuses.FirstOrDefault(s => s.StatusID == id)?.Name ?? Unknown("Status", id);

    /// <summary>Resolves a conventional status name (ConventionsOptions). Null when the row doesn't exist.</summary>
    public Status? FindStatus(string name) =>
        Statuses.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves a conventional artifact-type name (ConventionsOptions). Null when the row doesn't exist.</summary>
    public ArtifactType? FindArtifactType(string name) =>
        ArtifactTypes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    // A dangling ID is shown, not hidden: it means the lookup changed or failed to load.
    private static string Unknown(string entity, int id) => $"{entity} #{id} (unknown)";
}
