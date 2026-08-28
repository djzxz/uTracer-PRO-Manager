using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed class ProfileRepository
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public async Task<IReadOnlyList<TubeProfile>> LoadAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<TubeProfile> result;
		await using (FileStream stream = File.OpenRead(path))
		{
			List<TubeProfile> list = await JsonSerializer.DeserializeAsync<List<TubeProfile>>(stream, Options, cancellationToken);
			if (list == null || list.Count == 0)
			{
				throw new InvalidDataException("Biblioteka profili jest pusta.");
			}
			result = list.OrderBy((TubeProfile profile) => profile.DisplayName).ToArray();
		}
		return result;
	}
}
