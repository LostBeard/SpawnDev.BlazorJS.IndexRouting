// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SpawnDev.BlazorJS.IndexRouting.Routing;

/// <summary>
/// Provides information about the current asynchronous navigation event
/// including the target path and the cancellation token.
/// </summary>
public sealed class NavigationContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="NavigationContext"/>.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="cancellationToken"></param>
    public NavigationContext(string path, CancellationToken cancellationToken)
    {
        Path = path;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// The target path for the navigation.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The <see cref="CancellationToken"/> to use to cancel navigation.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
