using uTracerProManager.Core.Models;

namespace uTracerProManager.Models;

public sealed class TubeCatalogSearchItem
{
	public TubeProfile? Profile { get; }

	public FrankCatalogEntry? Datasheet { get; }

	public bool IsFavorite { get; }

	public bool IsMeasurementProfile => Profile != null;

	public string StableId
	{
		get
		{
			if (Profile == null)
			{
				return "datasheet:" + Datasheet.DataSheetUrl;
			}
			return "profile:" + Profile.Id;
		}
	}

	public string DisplayName
	{
		get
		{
			string value = (IsFavorite ? "★ " : string.Empty);
			if (Profile != null)
			{
				string value2 = (Profile.IsUserDefined ? "PROFIL RĘCZNY" : (Profile.ApprovedForHardware ? "ZWERYFIKOWANY PROFIL" : "PROFIL ZABLOKOWANY"));
				return $"{value}{Profile.DisplayName}  [{value2}]";
			}
			return $"{value}{Datasheet.TubeType} — {Datasheet.Manufacturer}  [{Datasheet.MeasurementStatusLabel}]";
		}
	}

	private TubeCatalogSearchItem(TubeProfile? profile, FrankCatalogEntry? datasheet, bool isFavorite)
	{
		Profile = profile;
		Datasheet = datasheet;
		IsFavorite = isFavorite;
	}

	public static TubeCatalogSearchItem FromProfile(TubeProfile profile, bool isFavorite)
	{
		return new TubeCatalogSearchItem(profile, null, isFavorite);
	}

	public static TubeCatalogSearchItem FromDatasheet(FrankCatalogEntry datasheet, bool isFavorite)
	{
		return new TubeCatalogSearchItem(null, datasheet, isFavorite);
	}
}
