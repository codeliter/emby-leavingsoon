using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Emby.Plugin.LeavingSoon.Configuration;

namespace Emby.Plugin.LeavingSoon;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public override Guid Id => new("7f3a9c2e-4b1d-4e8f-9a6c-2d5b8e1f3a07");

    public override string Name => "Leaving Soon";

    public override string Description =>
        "Surfaces movies and seasons nobody has watched in a while in a collection, then coordinates removal through Sonarr and Radarr.";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "leavingsoon",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
        yield return new PluginPageInfo
        {
            Name = "LeavingSoonConfigPageJS",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
        };
        yield return new PluginPageInfo
        {
            Name = "leavingsoonpage",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.leavingSoonPage.html",
            EnableInMainMenu = true,
            DisplayName = "Leaving Soon",
            MenuIcon = "hourglass_empty"
        };
        yield return new PluginPageInfo
        {
            Name = "LeavingSoonPageJS",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.leavingSoonPage.js"
        };
    }
}
