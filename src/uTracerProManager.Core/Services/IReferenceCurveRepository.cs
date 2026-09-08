using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public interface IReferenceCurveRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ReferenceCurveSeries series, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReferenceCurveSeries>> FindMatchingAsync(
        ReferenceCurveMatchRequest request,
        CancellationToken cancellationToken = default);
}
