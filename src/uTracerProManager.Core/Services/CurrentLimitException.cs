using System;

namespace uTracerProManager.Core.Services;

public sealed class CurrentLimitException : InvalidOperationException
{
	public CurrentLimitException(string message)
		: base(message)
	{
	}
}
