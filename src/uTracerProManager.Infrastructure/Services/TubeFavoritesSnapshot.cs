using System.Collections.Generic;

namespace uTracerProManager.Services;

public sealed record TubeFavoritesSnapshot(IReadOnlySet<string> ProfileIds, IReadOnlySet<string> DatasheetUrls)
{
	public int TotalCount => ProfileIds.Count + DatasheetUrls.Count;

	public bool IsProfileFavorite(string profileId)
	{
		return ProfileIds.Contains(profileId);
	}

	public bool IsDatasheetFavorite(string dataSheetUrl)
	{
		return DatasheetUrls.Contains(TubeFavoritesService.NormalizeDatasheetUrl(dataSheetUrl));
	}
}
