namespace CodePublisher.Features.NuGet.Validation;

/// <summary>
/// Minimal PE/CLR header check that answers "is this file really a managed .NET assembly?"
/// without loading it: MZ magic, PE signature, and a non-empty CLR header (data directory 14).
/// Pure byte inspection, so tests feed it real and fake DLLs.
/// </summary>
public static class ManagedAssemblyInspector
{
    public static bool IsManagedAssembly(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return IsManagedAssembly(stream);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsManagedAssembly(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) // "MZ"
        {
            return false;
        }

        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset + 24 > stream.Length)
        {
            return false;
        }

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
        {
            return false;
        }

        // COFF header: machine (2), sections (2), ... then the optional header.
        stream.Position = peOffset + 4;
        _ = reader.ReadUInt16(); // machine
        var sectionCount = reader.ReadUInt16();
        stream.Position = peOffset + 20;
        var optionalHeaderSize = reader.ReadUInt16();
        var optionalHeaderOffset = peOffset + 24;
        if (optionalHeaderSize == 0 || optionalHeaderOffset + optionalHeaderSize > stream.Length)
        {
            return false;
        }

        stream.Position = optionalHeaderOffset;
        var magic = reader.ReadUInt16();
        int dataDirectoryOffset;
        if (magic == 0x10B) // PE32
        {
            dataDirectoryOffset = optionalHeaderOffset + 96;
        }
        else if (magic == 0x20B) // PE32+
        {
            dataDirectoryOffset = optionalHeaderOffset + 112;
        }
        else
        {
            return false;
        }

        // Data directory 14 is the CLR runtime header; a real managed assembly has one.
        var clrDirectoryOffset = dataDirectoryOffset + 14 * 8;
        if (clrDirectoryOffset + 8 > stream.Length)
        {
            return false;
        }

        stream.Position = clrDirectoryOffset;
        return reader.ReadUInt32() != 0; // CLR header RVA
    }
}
