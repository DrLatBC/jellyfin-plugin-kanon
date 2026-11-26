using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;

using Newtonsoft.Json.Linq;
using Jellyfin.Plugin.Kanon.Helpers;

namespace Jellyfin.Plugin.Kanon.Tasks;

/// <summary>
/// Startup task responsible for registering UI script and style injections
/// using the FileTransformation plugin, enabling the Kanon front-end integration.
/// </summary>
public class StartupTask : IScheduledTask
{
    private readonly ILogger<StartupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupTask"/> class.
    /// </summary>
    /// <param name="logger">Injected logger instance for diagnostic output.</param>
    public StartupTask(ILogger<StartupTask> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Front-End Startup";
    /// <inheritdoc />
    public string Key => "KanonStartup";
    /// <inheritdoc />
    public string Description => "Registers UI script and style injections for Kanon.";
    /// <inheritdoc />
    public string Category => "Kanon";

    /// <summary>
    /// Returns the default triggers for this task.  
    /// This task executes once at Jellyfin startup.
    /// </summary>
    /// <returns>A collection containing a single startup trigger.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }

    /// <summary>
    /// Executes the startup process that registers one or more
    /// file transformation callbacks into the FileTransformation plugin.
    /// This enables the plugin to inject custom JavaScript and CSS into Jellyfin's index.html.
    /// </summary>
    /// <param name="progress">Progress reporter (unused).</param>
    /// <param name="cancellationToken">Cancellation token (unused).</param>
    /// <returns>A completed task when the process finishes.</returns>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Kanon startup: registering frontend transformations.");

        var payload = new JObject();
        payload.Add("id", "d1a727c4-7183-40cf-bb06-01a2177dc889");
        payload.Add("fileNamePattern", "index.html");
        payload.Add("callbackAssembly", GetType().Assembly.FullName);
        payload.Add("callbackClass", typeof(TransformationPatch).FullName);
        payload.Add("callbackMethod", nameof(TransformationPatch.InjectIntoIndexHtml));


        Assembly? fileTransformationAssembly =
            AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) ?? false);

        if (fileTransformationAssembly == null)
        {
            _logger.LogWarning("Kanon: FileTransformation plugin not found. UI injection will not work.");
            return Task.CompletedTask;
        }

        Type? pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");

        if (pluginInterfaceType == null)
        {
            _logger.LogWarning("Kanon: FileTransformation PluginInterface not found.");
            return Task.CompletedTask;
        }

        pluginInterfaceType.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { payload });

        _logger.LogInformation("Kanon: transformations registered successfully.");
        return Task.CompletedTask;
    }
}