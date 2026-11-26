using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Kanon.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Kanon;

/// <summary>
/// Kanon Plugin. Checks your anime on animefillerlist.com and AniDb and updates episode tags.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
{

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="xmlSerializer">The XML serializer used by Jellyfin.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Kanon";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("4fac206b-6073-4e88-b584-e22e1c53d9e4");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Returns information about the web pages exposed by this plugin for the Jellyfin dashboard.
    /// </summary>
    /// <returns>A collection of <see cref="PluginPageInfo"/> objects.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        string? prefix = GetType().Namespace;

        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{prefix}.Configuration.configPage.html",
            EnableInMainMenu = true,
            MenuIcon = "fast_forward"
        };
    }
}