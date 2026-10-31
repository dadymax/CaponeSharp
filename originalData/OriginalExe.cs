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
        // Data directory 2: the resource tree.
        _resourceVa = _imageBase + BitConverter.ToUInt32(_data, pe + 24 + 96 + 2 * 8);
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

    private uint _resourceVa;

    /// <summary>
    /// The data of a PE resource: <paramref name="type"/> (1 cursor, 12 cursor group…), then a name or an id, first
    /// language. Null if missing.
    /// </summary>
    public byte[] ReadResource(int type, string name = null, int id = -1)
    {
        if (_data == null || _resourceVa == _imageBase)
            return null;
        var root = Offset(_resourceVa);
        if (root < 0)
            return null;
        var typeDir = FindEntry(root, root, null, type);
        var nameDir = typeDir < 0 ? -1 : FindEntry(root, typeDir, name, id);
        var leaf = nameDir < 0 ? -1 : FindEntry(root, nameDir, null, -1);
        if (leaf < 0)
            return null;
        var rva = BitConverter.ToUInt32(_data, leaf);
        var size = BitConverter.ToInt32(_data, leaf + 4);
        var at = Offset(_imageBase + rva);
        if (at < 0 || at + size > _data.Length)
            return null;
        var result = new byte[size];
        Array.Copy(_data, at, result, 0, size);
        return result;
    }

    /// <summary>
    /// The child of resource directory <paramref name="dir"/> with that name or id (both null / -1: the first one).
    /// Returns the child's offset (a directory, or for a leaf its data entry); -1 if missing.
    /// </summary>
    private int FindEntry(int root, int dir, string name, int id)
    {
        int named = BitConverter.ToUInt16(_data, dir + 12), ids = BitConverter.ToUInt16(_data, dir + 14);
        for (var k = 0; k < named + ids; k++)
        {
            var nameField = BitConverter.ToUInt32(_data, dir + 16 + 8 * k);
            var dataField = BitConverter.ToUInt32(_data, dir + 16 + 8 * k + 4);
            bool match;
            if ((nameField & 0x80000000) != 0)
            {
                var at = root + (int)(nameField & 0x7FFFFFFF);
                var length = BitConverter.ToUInt16(_data, at);
                var entryName = Encoding.Unicode.GetString(_data, at + 2, length * 2);
                match = name == null ? id < 0 : string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase);
            }
            else
                match = name == null && (id < 0 || nameField == id);
            if (match)
                return root + (int)(dataField & 0x7FFFFFFF);
        }
        return -1;
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
