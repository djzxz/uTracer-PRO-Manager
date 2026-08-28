using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed class UserTubeProfileService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	private readonly string _path;

	public UserTubeProfileService()
	{
		_path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uTracerProManager", "user_profiles.json");
	}

	public async Task<IReadOnlyList<TubeProfile>> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(_path))
		{
			return Array.Empty<TubeProfile>();
		}
		IReadOnlyList<TubeProfile> result;
		await using (FileStream stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
		{
			result = ((await JsonSerializer.DeserializeAsync<List<TubeProfile>>(stream, JsonOptions, cancellationToken)) ?? new List<TubeProfile>()).Where((TubeProfile profile) => profile.IsUserDefined).OrderBy<TubeProfile, string>((TubeProfile profile) => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
		}
		return result;
	}

	public async Task SaveAsync(TubeProfile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		if (!profile.IsUserDefined)
		{
			throw new InvalidOperationException("Do pliku użytkownika można zapisać tylko profil ręczny.");
		}
		List<TubeProfile> list = (await LoadAsync(cancellationToken)).ToList();
		int num = list.FindIndex((TubeProfile item) => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
		if (num >= 0)
		{
			list[num] = profile;
		}
		else
		{
			list.Add(profile);
		}
		list.Sort((TubeProfile left, TubeProfile right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
		string directoryName = Path.GetDirectoryName(_path);
		Directory.CreateDirectory(directoryName);
		string temporaryPath = Path.Combine(directoryName, $".user_profiles_{Guid.NewGuid():N}.tmp");
		try
		{
			await using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
			{
				await JsonSerializer.SerializeAsync(stream, list, JsonOptions, cancellationToken);
				await stream.FlushAsync(cancellationToken);
			}
			File.Move(temporaryPath, _path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				try
				{
					File.Delete(temporaryPath);
				}
				catch (IOException)
				{
				}
			}
		}
	}
}
