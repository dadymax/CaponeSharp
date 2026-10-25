using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Chicago.tile: list of bitmaps, then one line per tile (bitmap index, size, frame coordinates).</summary>
public class OriginalTiles
{
    public List<SingleTileLine> Tiles { get; } = new();
    public List<string> Bmps { get; } = new();

    private static OriginalTiles _instance;
    public static OriginalTiles Instance => _instance ??= new OriginalTiles();

    private OriginalTiles()
    {
        var path = OriginalData.Instance.OriginalGameDirectory + "/Chicago.tile";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("OriginalTiles: failed to open ", path);
            return;
        }

        var line = file.GetLine().Trim();
        if (!int.TryParse(line, out var countBmps))
        {
            GD.PrintErr("OriginalTiles: invalid BMP count: ", line);
            return;
        }
        for (var i = 0; i < countBmps; i++)
        {
            var bmpLine = file.GetLine().Trim();
            if (bmpLine.Length > 0)
                Bmps.Add(bmpLine.ToUpperInvariant());
        }

        line = file.GetLine().Trim();
        if (!int.TryParse(line, out var countTiles))
        {
            GD.PrintErr("OriginalTiles: invalid tile count: ", line);
            return;
        }
        for (var i = 0; i < countTiles; i++)
        {
            var tileLine = file.GetLine();
            if (tileLine.Trim().Length == 0)
                continue;
            var tile = new SingleTileLine();
            if (tile.ProcessLine(tileLine))
                Tiles.Add(tile);
            else
                GD.PrintErr("OriginalTiles: failed to parse tile line: ", tileLine);
        }
    }

    /// <summary>First tile whose comment equals the given text (case-insensitive), or null.</summary>
    public SingleTileLine GetRecordWithComment(string comment)
    {
        var search = comment.ToUpperInvariant();
        foreach (var tile in Tiles)
            if (tile.Comment == search)
                return tile;
        return null;
    }

    /// <summary>Tile by 0-based array index, or null.</summary>
    public SingleTileLine GetTileByIndex(int index)
    {
        if (index < 0 || index >= Tiles.Count)
        {
            GD.PrintErr($"OriginalTiles: tile index {index} out of range (0-{Tiles.Count - 1})");
            return null;
        }
        return Tiles[index];
    }

    public class SingleTileLine
    {
        public int BmpInd = -1;
        public Vector2I Size = Vector2I.Zero;
        public int NumOfPairs;
        public List<Vector2I> Pairs { get; } = new();
        public string Comment = "";

        private static readonly char[] Separators = { ' ', '\t' };

        /// <summary>Parses "A B C D E F [more pairs] ; comment". Returns false on bad data.</summary>
        public bool ProcessLine(string line)
        {
            var cleanLine = line;
            var commentIdx = line.IndexOf(';');
            if (commentIdx >= 0)
            {
                Comment = line[(commentIdx + 1)..].Trim().ToUpperInvariant();
                cleanLine = line[..commentIdx];
            }

            var parts = cleanLine.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
            {
                GD.PrintErr($"OriginalTiles: tile line too short ({parts.Length} parts): {line}");
                return false;
            }

            if (!int.TryParse(parts[0], out BmpInd))
            {
                GD.PrintErr("OriginalTiles: invalid BmpInd: ", parts[0]);
                return false;
            }
            if (!int.TryParse(parts[1], out var sx) || !int.TryParse(parts[2], out var sy))
            {
                GD.PrintErr("OriginalTiles: invalid Size: ", parts[1], " ", parts[2]);
                return false;
            }
            Size = new Vector2I(sx, sy);
            if (Size.X <= 0 || Size.Y <= 0)
            {
                GD.PrintErr("OriginalTiles: invalid tile size: ", Size);
                return false;
            }
            if (!int.TryParse(parts[3], out NumOfPairs) || NumOfPairs < 0)
            {
                GD.PrintErr("OriginalTiles: invalid NumOfPairs: ", parts[3]);
                return false;
            }

            var expectedParts = 4 + NumOfPairs * 2;
            if (parts.Length < expectedParts)
            {
                GD.PrintErr($"OriginalTiles: not enough pairs (expected {expectedParts}, got {parts.Length})");
                return false;
            }

            Pairs.Clear();
            for (var i = 0; i < NumOfPairs; i++)
            {
                var px = parts[4 + i * 2];
                var py = parts[4 + i * 2 + 1];
                if (!int.TryParse(px, out var x) || !int.TryParse(py, out var y))
                {
                    GD.PrintErr("OriginalTiles: invalid pair coordinates: ", px, " ", py);
                    return false;
                }
                Pairs.Add(new Vector2I(x, y));
            }
            return true;
        }
    }
}
