using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>All .map files from original_game/Maps, keyed by upper-case file name.</summary>
public class OriginalMaps
{
    private static OriginalMaps _instance;
    public static OriginalMaps Instance => _instance ??= new OriginalMaps();

    public Dictionary<string, Map> Maps { get; } = new();

    private OriginalMaps()
    {
        var path = OriginalData.Instance.OriginalGameDirectory + "/Maps";
        var files = DirAccess.GetFilesAt(path);
        if (files.Length == 0)
        {
            GD.PrintErr("OriginalMaps: no files found at ", path);
            return;
        }
        foreach (var file in files)
        {
            var map = Map.Create(path + "/" + file);
            Maps.TryAdd(map.FileName, map);
        }
    }

    public class Map
    {
        private static readonly char[] Separators = { ' ', '\t' };

        public string FileName;
        public string Ver;
        public Vector2I Dimensions;
        public List<Vector3I> BlockPlacement { get; } = new();
        public List<Vector2I> StartPoints { get; } = new();
        /// <summary>Problems found while reading the file.</summary>
        public List<string> Errors { get; } = new();

        private void Error(string message)
        {
            Errors.Add($"{FileName}: {message}");
            GD.PrintErr("OriginalMaps: ", FileName, ": ", message);
        }

        public static Map Create(string path)
        {
            var result = new Map();
            result.ReadFile(path);
            return result;
        }

        public void ReadFile(string path)
        {
            FileName = path.GetFile().ToUpperInvariant();
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Error("failed to open " + path);
                return;
            }

            Ver = file.GetLine().Trim();
            if (Ver is not ("VER01" or "VER02"))
                Error($"unknown version '{Ver}'");

            var xdim = ToInt(GetParts(SkipEmpty(file)), 1);
            var ydim = ToInt(GetParts(SkipEmpty(file)), 1);
            Dimensions = new Vector2I(xdim, ydim);

            var line = SkipEmpty(file);
            if (line.Trim() != "BLOCKS")
                Error($"expected BLOCKS, got '{line}'");
            while (!file.EofReached())
            {
                var parts = GetParts(SkipEmpty(file));
                var place = new Vector3I(ToInt(parts, 0), ToInt(parts, 1), ToInt(parts, 2));
                if (place.X == -1)
                    break;
                BlockPlacement.Add(place);
            }

            line = SkipEmpty(file);
            if (line.Trim() != "STARTPOINTS")
                Error($"expected STARTPOINTS, got '{line}'");
            while (!file.EofReached())
            {
                var parts = GetParts(SkipEmpty(file));
                var place = new Vector2I(ToInt(parts, 0), ToInt(parts, 1));
                if (place.X == -1)
                    break;
                StartPoints.Add(place);
            }
        }

        /// <summary>Next line that is neither blank nor a ' comment.</summary>
        private static string SkipEmpty(FileAccess file)
        {
            var line = file.GetLine();
            while ((line.Trim().Length == 0 || line.Trim().StartsWith('\'')) && !file.EofReached())
                line = file.GetLine();
            return line;
        }

        /// <summary>Splits on spaces/tabs after dropping a trailing ' comment.</summary>
        private static string[] GetParts(string line)
        {
            var commentIdx = line.IndexOf('\'');
            if (commentIdx >= 0)
                line = line[..commentIdx];
            return line.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static int ToInt(string[] parts, int index) =>
            index < parts.Length && int.TryParse(parts[index], out var v) ? v : -1;
    }
}
