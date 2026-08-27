using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Starlight.Common;
using Serilog;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Resources;

internal static partial class DataLoader
{
    /// <summary>
    /// Invokes the data loaders here, then outputs the data in the class's fields.
    /// </summary>
    public static void Initialize(GameData output)
    {
        // First pass of data loading.
        Task.WaitAll(
            Task.Run(() => LoadScenePoints(output)),
            Task.Run(() => LoadExcels(output))
        );

        // Second pass of data loading.
        Task.WaitAll(
            Task.Run(() => LoadAbilities(output))
        );
    }

    /// <summary>
    /// Loads all ExcelBinOutput (xlsx -> json) files.
    /// </summary>
    private static void LoadExcels(GameData output)
    {
        var stopwatch = Stopwatch.StartNew();

        var resources = typeof(DataLoader).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<GameResource>()
                .Select(attr => (t, attr)))
            .OrderByDescending(t => t.attr.Priority)
            .ToList();

        foreach (var (type, info) in resources)
        {
            var filePath = $"ExcelBinOutput/{info.FileName}";
            var typeName = type.Name;

            if (typeof(GameData)
                    .GetField(typeName, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(output) is not
                IDictionary dictionary)
            {
                Log.Warning("Resource {0} has an invalid type.", typeName);
                continue;
            }

            switch (filePath.FileExtension())
            {
                case "json":
                    var listType = typeof(List<>).MakeGenericType(type);

                    var data = Resources.Loader.ReadJson(filePath, listType);

                    if (data is not IList list)
                    {
                        Log.Warning("Failed to load resource file: {0}", filePath);
                        continue;
                    }

                    foreach (var item in list)
                    {
                        if (item is not Data resource) continue;

                        var id = resource.GetId();

                        resource.OnLoad();

                        if (dictionary.Contains(id))
                        {
                            Log.Warning("Resource {0} has a value in the dictionary!", id);
                        }
                        dictionary[id] = resource;
                    }
                    break;
                case "tsv":
                    throw new Exception("TSV files are not supported.");
                case "tsj":
                    throw new Exception("TSJ files are not supported.");
                default:
                    Log.Warning("Unknown resource file extension: {0}", filePath);
                    continue;
            }
        }

        Log.Information("Loading excel resources took {0}ms", stopwatch.ElapsedMilliseconds);
    }

    #region Binary Data

    /// <summary>
    /// Loads all abilities for avatars, monsters, and activities.
    /// </summary>
    private static void LoadAbilities(GameData output)
    {
        var regex = AbilityRegex();
        var mapping = output.AvatarData
            .ToDictionary(kvp => kvp.Value.AvatarName, kvp => kvp.Key);

        var stopwatch = Stopwatch.StartNew();

        Resources.Loader.ListFiles("BinOutput/Ability/Temp", "ConfigAbility_*.json", true)
            .Select((string? type, string? name, List<Ability> abilities) (p) => {
                var match = regex.Match(p);

                if (!match.Success)
                {
                    return (null, null, []);
                }

                var type = match.Groups["type"].Value;
                var name = match.Groups["name"].Value;
                var data = Resources.Loader.ReadJson<List<Ability>>(p);

                return data is null ? (null, null, []) : (type, name, data);
            })
            .OfType<(string type, string name, List<Ability> abilities)>()
            .ToList()
            .ForEach(p => {
                switch (p.type)
                {
                    case "Avatar":
                        if (!mapping.TryGetValue(p.name, out var avatarId)) return;

                        output.AvatarAbilities[avatarId] = p.abilities;
                        break;
                }
            });

        Log.Verbose("Loading abilities took {0}ms with {1} entries", stopwatch.ElapsedMilliseconds, output.AvatarAbilities.Count);
    }

    /// <summary>
    /// Loads all teleport waypoints for all scenes.
    /// </summary>
    private static void LoadScenePoints(GameData output)
    {
        var regex = ScenePointRegex();
        var stopwatch = Stopwatch.StartNew();

        Resources.Loader.ListFiles("BinOutput/Scene/Point", "scene*_point.json")
            .Select((uint sceneId, ScenePointConfig? data) (p) => {
                var match = regex.Match(p);

                if (!match.Success)
                {
                    return (uint.MinValue, null);
                }

                var sceneId = uint.Parse(match.Groups[1].Value);
                var data = Resources.Loader.ReadJson<ScenePointConfig>(p);

                if (data?.Points is null)
                {
                    return (uint.MinValue, null);
                }

                foreach (var (pointId, point) in data.Points)
                {
                    point.PointId = uint.Parse(pointId);
                    point.SceneId = sceneId;
                }

                return (sceneId, data);
            })
            .Where(d => d.data is not null)
            .Select(d => {
                var data = new Dictionary<uint, PointData>();

                foreach (var (_, point) in d.data!.Points)
                {
                    data.Add(point.PointId, point);
                }

                return (d.sceneId, data);
            })
            .ToList()
            .ForEach(d => output.ScenePoints[d.sceneId] = d.data);

        Log.Verbose("Loading scene points took {0}ms with {1} entries", stopwatch.ElapsedMilliseconds, output.ScenePoints.Count);
    }

    #endregion

    #region Expressions

    [GeneratedRegex("ConfigAbility_(?<type>)_(?<name>)")]
    private static partial Regex AbilityRegex();

    [GeneratedRegex(@"scene([0-9]+)_point\.json")]
    private static partial Regex ScenePointRegex();

    #endregion
}
