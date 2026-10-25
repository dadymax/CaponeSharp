using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace Capone;

/// <summary>
/// Read-only access to Capone.EXE by virtual address (as shown in disassembled/Capone.EXE.lst).
/// Game data that only exists inside the executable (tile blocks, unit table, texts) is read from here at runtime.
/// </summary>
public class OriginalExe
{
    public const string FileName = "Capone.EXE";

    private static OriginalExe _instance;
    public static OriginalExe Instance => _instance ??= new OriginalExe();

    /// <summary>Text encoding of the Russian edition.</summary>
    public static Encoding Cp1251 { get; }

    static OriginalExe()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>EN, ES and RU strings are stored next to each other in that order.</summary>
    public enum Language { English = 0, Spanish = 1, Russian = 2 }

    private readonly byte[] _data;
    private readonly List<Section> _sections = new();
    private uint _imageBase;

    public bool IsLoaded => _data != null;

    private record struct Section(string Name, uint VirtualAddress, uint VirtualSize, uint RawOffset, uint RawSize);

    private OriginalExe()
    {
        var path = OriginalData.Instance.OriginalGameDirectory + "/" + FileName;
        try
        {
            _data = File.ReadAllBytes(path);
            ParseHeaders();
        }
        catch (Exception e)
        {
            GD.PrintErr($"OriginalExe: cannot read {path}: {e.Message}");
            _data = null;
        }
    }

    private void ParseHeaders()
    {
        var pe = BitConverter.ToInt32(_data, 0x3C);
        if (_data[pe] != 'P' || _data[pe + 1] != 'E')
            throw new InvalidDataException("not a PE file");
        var sectionCount = BitConverter.ToUInt16(_data, pe + 6);
        var optionalHeaderSize = BitConverter.ToUInt16(_data, pe + 20);
        _imageBase = BitConverter.ToUInt32(_data, pe + 24 + 28);
        for (var i = 0; i < sectionCount; i++)
        {
            var o = pe + 24 + optionalHeaderSize + i * 40;
            _sections.Add(new Section(
                Encoding.ASCII.GetString(_data, o, 8).TrimEnd('\0'),
                _imageBase + BitConverter.ToUInt32(_data, o + 12),
                BitConverter.ToUInt32(_data, o + 8),
                BitConverter.ToUInt32(_data, o + 20),
                BitConverter.ToUInt32(_data, o + 16)));
        }
    }

    /// <summary>File offset of a virtual address, or -1 if it is outside the file's sections.</summary>
    public int Offset(uint va)
    {
        foreach (var s in _sections)
        {
            var size = Math.Max(s.VirtualSize, s.RawSize);
            if (va >= s.VirtualAddress && va < s.VirtualAddress + size)
            {
                var offset = va - s.VirtualAddress;
                return offset < s.RawSize ? (int)(s.RawOffset + offset) : -1;
            }
        }
        return -1;
    }

    public bool IsValidAddress(uint va) => _data != null && Offset(va) >= 0;

    private int Checked(uint va, int size)
    {
        var o = Offset(va);
        if (o < 0 || o + size > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(va), $"0x{va:X} is not in the file");
        return o;
    }

    public byte ReadU8(uint va) => _data[Checked(va, 1)];
    public ushort ReadU16(uint va) => BitConverter.ToUInt16(_data, Checked(va, 2));
    public short ReadI16(uint va) => BitConverter.ToInt16(_data, Checked(va, 2));
    public uint ReadU32(uint va) => BitConverter.ToUInt32(_data, Checked(va, 4));
    public int ReadI32(uint va) => BitConverter.ToInt32(_data, Checked(va, 4));
    public float ReadF32(uint va) => BitConverter.ToSingle(_data, Checked(va, 4));

    /// <summary>Zero-terminated string, decoded as CP1251.</summary>
    public string ReadCString(uint va, int maxLength = 4096)
    {
        var start = Checked(va, 1);
        var end = start;
        while (end < _data.Length && end - start < maxLength && _data[end] != 0)
            end++;
        return Cp1251.GetString(_data, start, end - start);
    }

    /// <summary>String through a pointer: reads the dword at <paramref name="pointerVa"/> and the string it points to.</summary>
    public string ReadStringPointer(uint pointerVa) => ReadCString(ReadU32(pointerVa));

    /// <summary>One entry of a trilingual string table (three consecutive string pointers EN/ES/RU).</summary>
    public string ReadTriple(uint tripleVa, Language language) => ReadStringPointer(tripleVa + (uint)language * 4);
}
