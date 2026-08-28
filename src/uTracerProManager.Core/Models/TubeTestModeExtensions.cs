namespace uTracerProManager.Core.Models;

public static class TubeTestModeExtensions
{
	public static string DisplayName(this TubeTestMode mode)
	{
		return mode switch
		{
			TubeTestMode.FullDiagnostic => "PEŁNA DIAGNOSTYKA", 
			TubeTestMode.NormalDual => "NORMALNY — OBIE POŁÓWKI", 
			TubeTestMode.Quick => "SZYBKI TEST", 
			_ => mode.ToString(), 
		};
	}

	public static string Description(this TubeTestMode mode)
	{
		return mode switch
		{
			TubeTestMode.FullDiagnostic => "60 s nagrzewania, test wstępny, automatyczna korekcja napięć, pomiar Ia/gm/Rp/μ obu połówek, 5 min stabilności termicznej i skan charakterystyk.", 
			TubeTestMode.NormalDual => "60 s nagrzewania, obie połówki mierzone równocześnie, automatyczna korekcja napięć, podstawowe Ia/gm/Rp/μ i porównanie A/B.", 
			TubeTestMode.Quick => "60 s nagrzewania i jeden skorygowany punkt katalogowy obu połówek. Służy do szybkiej selekcji seryjnej.", 
			_ => string.Empty, 
		};
	}
}
