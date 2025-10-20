// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

static partial class ThisAssembly
{
    /// <summary>
    /// Gets the name of this module.
    /// </summary>
    public static string Name { get; } = typeof(ThisAssembly).Assembly.GetName().Name!;

    /// <summary>
    /// Gets the version of this module.
    /// </summary>
    public static Version Version { get; } = typeof(ThisAssembly).Assembly.GetName().Version!;
}
