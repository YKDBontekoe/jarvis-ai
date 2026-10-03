using YamlDotNet.RepresentationModel;

/// <summary>
/// Adds the Compose settings Aspire's Compose model has no property for (a PID limit, <c>platform</c>) to the
/// published file. The values are defined in <see cref="ProductionDeployment.ExtraSettings"/>, so the AppHost stays
/// the only place the deployment is described.
/// </summary>
internal static class ComposeOutput
{
    public static void ApplyExtraSettings(string[] args,
        IReadOnlyDictionary<string, (int? PidsLimit, string? Platform)> settings)
    {
        var outputIndex = Array.IndexOf(args, "--output-path");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) return;
        var path = Path.Combine(args[outputIndex + 1], "docker-compose.yaml");
        if (!File.Exists(path)) throw new InvalidOperationException($"Aspire did not write {path}.");

        var stream = new YamlStream();
        using (var reader = new StreamReader(path)) stream.Load(reader);
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];
        foreach (var (name, (pidsLimit, platform)) in settings)
        {
            if (!services.Children.TryGetValue(new YamlScalarNode(name), out var node)) continue;
            var service = (YamlMappingNode)node;
            // Compose rejects pids_limit next to deploy.resources.limits, so the limit goes into that block.
            if (pidsLimit is { } pids) Limits(service).Children[new YamlScalarNode("pids")] = new YamlScalarNode(pids.ToString());
            if (platform is not null)
                service.Children[new YamlScalarNode("platform")] =
                    new YamlScalarNode(platform) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted };
        }

        using var writer = new StreamWriter(path);
        stream.Save(writer, assignAnchors: false);
    }

    private static YamlMappingNode Limits(YamlMappingNode service) =>
        Child(Child(Child(service, "deploy"), "resources"), "limits");

    private static YamlMappingNode Child(YamlMappingNode parent, string key)
    {
        if (parent.Children.TryGetValue(new YamlScalarNode(key), out var node)) return (YamlMappingNode)node;
        var created = new YamlMappingNode();
        parent.Children[new YamlScalarNode(key)] = created;
        return created;
    }
}
