using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// City-block layouts ("tile blocks"), read from Capone.EXE. A .map file places these blocks by number.
/// Table at 0x47C830, used by fnBuildingPlacementOnMapByNumber (0x420312): 38 records of 16 bytes:
/// +0 word width, +2 word height, +4 dword first Chicago.tile index, +8 dword category, +12 dword → array of row-string pointers.
/// Row characters: R road, S sidewalk, F non-walkable, B/C/M/H buildings, P police, D/G placeholders, I park.
/// </summary>
public class OriginalBlocks
{
    public const uint TableAddress = 0x47C830;
    public const int RecordSize = 16;
    public const int BlockCount = 38;

    private static OriginalBlocks _instance;
    public static OriginalBlocks Instance => _instance ??= new OriginalBlocks();

    public List<Block> Blocks { get; } = new();

    private OriginalBlocks()
    {
        var exe = OriginalExe.Instance;
        if (!exe.IsLoaded)
            return;

        for (var i = 0; i < BlockCount; i++)
        {
            var va = TableAddress + (uint)(i * RecordSize);
            var block = new Block
            {
                Number = i,
                X = exe.ReadU16(va),
                Y = exe.ReadU16(va + 2),
                Pos = exe.ReadI32(va + 4),
                Category = exe.ReadI32(va + 8),
            };
            var rows = exe.ReadU32(va + 12);
            if (block.X is <= 0 or > 64 || block.Y is <= 0 or > 64 || !exe.IsValidAddress(rows))
            {
                GD.PrintErr($"OriginalBlocks: bad record {i} at 0x{va:X}");
                break;
            }
            for (var y = 0; y < block.Y; y++)
            {
                var row = exe.ReadStringPointer(rows + (uint)(y * 4));
                if (row.Length != block.X)
                    GD.PrintErr($"OriginalBlocks: block {i} row {y} has length {row.Length}, expected {block.X}");
                block.BlockLines.Add(row);
            }
            Blocks.Add(block);
        }
    }

    public class Block
    {
        public int Number;
        /// <summary>Width in tiles.</summary>
        public int X;
        /// <summary>Height in tiles.</summary>
        public int Y;
        /// <summary>First tile index in Chicago.tile; the block's tiles follow in column order.</summary>
        public int Pos;
        /// <summary>Unknown category value (1 or 2 seen).</summary>
        public int Category;
        /// <summary>Y rows of X characters, top row first.</summary>
        public List<string> BlockLines { get; } = new();
    }
}
