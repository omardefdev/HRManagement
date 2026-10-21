using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// Ensures sidebar navigation links in each HR module _Layout resolve to existing MVC actions.
/// Prevents 404 regressions like wrong controller names in HR.Contracts or missing Dashboard controllers.
/// </summary>
public class ModuleSidebarNavTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void All_module_sidebar_layouts_reference_existing_controller_actions()
    {
        var root = ResolveRepoRoot();
        var servicesDir = Path.Combine(root, "src", "Services");
        var layouts = Directory.GetFiles(servicesDir, "_Layout.cshtml", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(layouts);

        var failures = new List<string>();
        foreach (var layoutPath in layouts)
        {
            var webProjectDir = layoutPath.Split($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}", 2)[0];
            var moduleLabel = webProjectDir.Replace(Path.Combine(root, "src", "Services") + Path.DirectorySeparatorChar, string.Empty);
            var actionsByController = LoadControllerActions(webProjectDir);
            var layoutSource = System.IO.File.ReadAllText(layoutPath);
            var links = Regex.Matches(layoutSource, @"asp-controller=""([^""]+)""\s+asp-action=""([^""]+)""");

            foreach (Match link in links)
            {
                var controller = link.Groups[1].Value;
                var action = link.Groups[2].Value;

                if (!actionsByController.TryGetValue(controller, out var actions))
                {
                    failures.Add($"{moduleLabel}: {controller}/{action} — controller not found");
                    continue;
                }

                if (!actions.Contains(action, StringComparer.Ordinal))
                    failures.Add($"{moduleLabel}: {controller}/{action} — action not found (available: {string.Join(", ", actions)})");
            }
        }

        Assert.True(failures.Count == 0, "Broken sidebar links:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Dashboard_views_reference_existing_controller_actions()
    {
        var root = ResolveRepoRoot();
        var servicesDir = Path.Combine(root, "src", "Services");
        var dashboardViews = Directory.GetFiles(servicesDir, "Index.cshtml", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}Dashboard{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(dashboardViews);

        var failures = new List<string>();
        foreach (var viewPath in dashboardViews)
        {
            var webProjectDir = viewPath.Split($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}", 2)[0];
            var moduleLabel = webProjectDir.Replace(Path.Combine(root, "src", "Services") + Path.DirectorySeparatorChar, string.Empty);
            var actionsByController = LoadControllerActions(webProjectDir);
            var source = System.IO.File.ReadAllText(viewPath);
            var links = Regex.Matches(source, @"asp-controller=""([^""]+)""\s+asp-action=""([^""]+)""");

            foreach (Match link in links)
            {
                var controller = link.Groups[1].Value;
                var action = link.Groups[2].Value;

                if (!actionsByController.TryGetValue(controller, out var actions))
                {
                    failures.Add($"{moduleLabel} Dashboard: {controller}/{action} — controller not found");
                    continue;
                }

                if (!actions.Contains(action, StringComparer.Ordinal))
                    failures.Add($"{moduleLabel} Dashboard: {controller}/{action} — action not found");
            }
        }

        Assert.True(failures.Count == 0, "Broken dashboard links:\n" + string.Join("\n", failures));
    }

    private static Dictionary<string, HashSet<string>> LoadControllerActions(string webProjectDir)
    {
        var controllersDir = Path.Combine(webProjectDir, "Controllers");
        if (!Directory.Exists(controllersDir))
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var allSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(controllersDir, "*.cs").Select(System.IO.File.ReadAllText));

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match controllerMatch in Regex.Matches(allSource, @"class\s+(\w+)Controller\b"))
        {
            var controllerName = controllerMatch.Groups[1].Value;
            var classBody = ExtractBalancedBlock(allSource, controllerMatch.Index);
            var actions = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match actionMatch in Regex.Matches(classBody, @"public\s+(?:async\s+)?Task<\s*IActionResult\s*>\s+(\w+)\s*\("))
                actions.Add(actionMatch.Groups[1].Value);

            foreach (Match actionMatch in Regex.Matches(classBody, @"public\s+IActionResult\s+(\w+)\s*\("))
                actions.Add(actionMatch.Groups[1].Value);

            result[controllerName] = actions;
        }

        return result;
    }

    private static string ExtractBalancedBlock(string source, int startIndex)
    {
        var brace = source.IndexOf('{', startIndex);
        if (brace < 0)
            return string.Empty;

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[startIndex..(i + 1)];
            }
        }

        return string.Empty;
    }
}
