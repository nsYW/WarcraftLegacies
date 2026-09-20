using System.Text.Json;
using FluentAssertions;

namespace WarcraftLegacies.Map.Tests;

/// <summary>
/// A locale overlay states the text a client in that language reads differently, and nothing else.
/// <para>
/// This was not true of the Chinese overlay: it was rebuilt from each base record's whole field list and carried
/// 60,463 values that are keys or statistics - sound sets, model paths, id lists, hit points, mana costs - and 340
/// of them had stopped agreeing with the branch it was to merge into. A client in that language would have played
/// with the old numbers, and every balance change upstream would have collided with the overlay.
/// </para>
/// <para>
/// Both checks below fail on a field that is not text, and on text stated although it reads the same as the base's:
/// an entry for either overrides nothing and only gets in the way of the next change to that data.
/// </para>
/// </summary>
public sealed class LocaleOverlayTests
{
  /// <summary>
  /// The fields a translation may write: names, tooltips, descriptions, editor suffixes. Anything else is a key
  /// (a sound set, a model path, an icon), an id list, or a statistic.
  /// </summary>
  private static readonly HashSet<string> TextFields = new(StringComparer.Ordinal)
  {
    "unam", "upro", "unsf", "utip", "utub", "utpr", "uawt", "ides", "iub1", "iico",
    "anam", "ansf", "aub1", "atp1", "arut", "aret", "aut1", "auu1",
    "gnam", "gnsf", "gub1", "gtp1",
    "fnam", "fnsf", "ftip", "fube",
    "bnam", "bsuf",
  };

  private static readonly string[] Kinds =
  {
    "UnitData", "AbilityData", "UpgradeData", "ItemData", "BuffData", "DestructableData",
  };

  [Fact]
  public void Overlay_StatesOnlyTextFields()
  {
    var offenders = new List<string>();

    foreach (var (overlayPath, _) in Overlays())
    {
      offenders.AddRange(from slot in Modifications(overlayPath)
                         where !TextFields.Contains(slot.Field)
                         select $"{Relative(overlayPath)}: {slot.Field}");
    }

    offenders
      .Should()
      .BeEmpty("a locale overlay may only override text; {0} value(s) are a key or a statistic", offenders.Count);
  }

  [Fact]
  public void Overlay_StatesOnlyTextThatDiffersFromTheBase()
  {
    var offenders = new List<string>();

    foreach (var (overlayPath, basePath) in Overlays())
    {
      var baseValues = Modifications(basePath).ToDictionary(slot => (slot.Field, slot.Level), slot => slot.Value);
      if (baseValues.Values.Any(ContainsChinese))
      {
        // The base records of a localised build tree have been shipped Chinese; this check reads the English.
        continue;
      }

      offenders.AddRange(from slot in Modifications(overlayPath)
                         where baseValues.TryGetValue((slot.Field, slot.Level), out var english) && english == slot.Value
                         select $"{Relative(overlayPath)}: {slot.Field} level {slot.Level}");
    }

    offenders
      .Should()
      .BeEmpty("text that reads the same as the base overrides nothing; {0} value(s) repeat it", offenders.Count);
  }

  private static IEnumerable<(string OverlayPath, string BasePath)> Overlays()
  {
    var mapData = Path.Combine(RepositoryRoot(), "mapdata", "WarcraftLegacies");

    foreach (var kind in Kinds)
    {
      var overlayDirectory = Path.Combine(mapData, kind + ".zhCN");
      if (!Directory.Exists(overlayDirectory))
      {
        continue;
      }

      foreach (var overlayPath in Directory.EnumerateFiles(overlayDirectory, "*.json"))
      {
        yield return (overlayPath, Path.Combine(mapData, kind, Path.GetFileName(overlayPath)));
      }
    }
  }

  private static IEnumerable<(string Field, int? Level, string Value)> Modifications(string path)
  {
    if (!File.Exists(path))
    {
      yield break;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (!document.RootElement.TryGetProperty("Modifications", out var modifications))
    {
      yield break;
    }

    foreach (var modification in modifications.EnumerateArray())
    {
      if (!modification.TryGetProperty("Id", out var id) ||
          !modification.TryGetProperty("Value", out var value) ||
          value.ValueKind != JsonValueKind.String)
      {
        continue;
      }

      var level = modification.TryGetProperty("Level", out var levelElement) &&
                  levelElement.ValueKind == JsonValueKind.Number
        ? levelElement.GetInt32()
        : (int?)null;

      yield return (id.GetString() ?? "", level, value.GetString() ?? "");
    }
  }

  private static bool ContainsChinese(string text)
  {
    return text.Any(character => character is >= '\u4e00' and <= '\u9fff');
  }

  /// <summary>
  /// Walks up from the test binaries until it finds the repository, which is where `mapdata` lives.
  /// </summary>
  private static string RepositoryRoot()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
      if (Directory.Exists(Path.Combine(directory.FullName, "mapdata", "WarcraftLegacies")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException($"Could not find the repository above {AppContext.BaseDirectory}.");
  }

  private static string Relative(string path)
  {
    return Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
  }
}
