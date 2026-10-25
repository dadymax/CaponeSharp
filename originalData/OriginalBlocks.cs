using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Tile-block layouts. For now read from the hand-made bldngs_parsed.txt;
/// eventually they must be extracted from Capone.EXE.
/// </summary>
public class OriginalBlocks
{
    private static OriginalBlocks _instance;
    public static OriginalBlocks Instance => _instance ??= new OriginalBlocks();

    public List<Block> Blocks { get; } = new();

    private OriginalBlocks()
    {
        using var file = FileAccess.Open("res://bldngs_parsed.txt", FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("OriginalBlocks: failed to open res://bldngs_parsed.txt");
            return;
        }
        var block = new Block();
        while (block.ProcessLine(file))
        {
            Blocks.Add(block);
            block = new Block();
        }
    }

    public class Block
    {
        public int X;
        public int Y;
        /// <summary>First line index in Chicago.tile.</summary>
        public int Pos;
        public string[] Sixnumbers;
        public int ThirdNumber;
        public List<string> BlockLines { get; } = new();

        /// <summary>Reads header "W H tileLine A B C D E F offset" (hex) and H layout rows.</summary>
        public bool ProcessLine(FileAccess file)
        {
            while (!file.EofReached())
            {
                var line = file.GetLine();
                if (line.Length == 0)
                    continue;
                var parts = line.Split(' ');
                X = parts[0].HexToInt();
                Y = parts[1].HexToInt();
                Pos = parts[2].HexToInt();
                Sixnumbers = parts[3..];
                ThirdNumber = parts[5].HexToInt();
                for (var i = 0; i < Y; i++)
                    BlockLines.Add(file.GetLine());
                return true;
            }
            return false;
        }
    }
}
