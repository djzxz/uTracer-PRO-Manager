namespace uTracerProManager.Services;

public sealed record TubeCatalogInfo(string SchemaVersion, string CatalogVersion, string Source, int ProfileCount, int ReadyProfileCount, int DatasheetCount, int ModelCount, int ManufacturerCount, int LinkedDatasheetCount, int ReadyDatasheetCount, int LinkedModelCount, int ReadyModelCount, string DatabasePath);
