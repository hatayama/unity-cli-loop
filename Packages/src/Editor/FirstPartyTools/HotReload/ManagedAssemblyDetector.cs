using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects whether a PE file carries managed (CLR) metadata by reading its headers.
    /// Why: on Windows the bundled shared-framework directory mixes native PE images and
    /// managed assemblies under the same .dll extension, so reference-set builders need a
    /// cheap managed-or-native check that does not load the file as an assembly.
    /// </summary>
    internal static class ManagedAssemblyDetector
    {
        private const int DosHeaderSize = 0x40;
        private const int PeSignatureOffsetLocation = 0x3C;
        private const int PeSignatureSize = 4;
        private const int CoffHeaderSize = 20;
        private const int SizeOfOptionalHeaderOffsetInCoff = 16;
        private const ushort Pe32Magic = 0x10B;
        private const ushort Pe32PlusMagic = 0x20B;
        private const int Pe32RvaCountOffset = 92;
        private const int Pe32DataDirectoriesOffset = 96;
        private const int Pe32PlusRvaCountOffset = 108;
        private const int Pe32PlusDataDirectoriesOffset = 112;
        private const int ClrRuntimeHeaderDirectoryIndex = 14;
        private const int DataDirectoryEntrySize = 8;

        /// <summary>
        /// Returns true when the file is a PE image whose optional header declares a CLR
        /// runtime header (data directory 14), i.e. a managed assembly. Malformed and native
        /// images return false; genuinely unreadable files throw (Fail Fast).
        /// </summary>
        public static bool IsManagedAssembly(string filePath)
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);

            long coffHeaderStart = ReadCoffHeaderStart(stream, reader);
            if (coffHeaderStart < 0)
            {
                return false;
            }

            stream.Position = coffHeaderStart + SizeOfOptionalHeaderOffsetInCoff;
            ushort optionalHeaderSize = reader.ReadUInt16();
            long optionalHeaderStart = coffHeaderStart + CoffHeaderSize;
            if (optionalHeaderSize < sizeof(ushort)
                || optionalHeaderStart + optionalHeaderSize > stream.Length)
            {
                return false;
            }

            stream.Position = optionalHeaderStart;
            ushort optionalHeaderMagic = reader.ReadUInt16();
            int rvaCountOffset = ResolveRvaCountOffset(optionalHeaderMagic);
            int dataDirectoriesOffset = ResolveDataDirectoriesOffset(optionalHeaderMagic);
            if (rvaCountOffset < 0 || dataDirectoriesOffset < 0)
            {
                return false;
            }

            int clrDirectoryOffset =
                dataDirectoriesOffset + ClrRuntimeHeaderDirectoryIndex * DataDirectoryEntrySize;
            if (optionalHeaderSize < clrDirectoryOffset + DataDirectoryEntrySize)
            {
                return false;
            }

            stream.Position = optionalHeaderStart + rvaCountOffset;
            uint rvaCount = reader.ReadUInt32();
            if (rvaCount <= ClrRuntimeHeaderDirectoryIndex)
            {
                return false;
            }

            stream.Position = optionalHeaderStart + clrDirectoryOffset;
            uint clrRuntimeHeaderRva = reader.ReadUInt32();
            uint clrRuntimeHeaderSize = reader.ReadUInt32();
            // A non-zero RVA paired with a zero size is a malformed image (real compilers emit
            // size 72), so it is rejected per the malformed-returns-false policy.
            return clrRuntimeHeaderRva != 0 && clrRuntimeHeaderSize != 0;
        }

        // Validates the DOS header, PE signature offset, and PE signature, and returns the
        // COFF header start offset; -1 means the preamble is not a well-formed PE image.
        private static long ReadCoffHeaderStart(FileStream stream, BinaryReader reader)
        {
            if (stream.Length < DosHeaderSize)
            {
                return -1;
            }

            // DOS header magic 'M','Z'.
            if (reader.ReadByte() != 0x4D || reader.ReadByte() != 0x5A)
            {
                return -1;
            }

            stream.Position = PeSignatureOffsetLocation;
            int peSignatureOffset = reader.ReadInt32();
            if (peSignatureOffset < DosHeaderSize
                || peSignatureOffset > stream.Length - PeSignatureSize - CoffHeaderSize)
            {
                return -1;
            }

            // PE signature 'P','E','\0','\0'.
            stream.Position = peSignatureOffset;
            if (reader.ReadByte() != 0x50 || reader.ReadByte() != 0x45
                || reader.ReadByte() != 0 || reader.ReadByte() != 0)
            {
                return -1;
            }

            return peSignatureOffset + PeSignatureSize;
        }

        // -1 means the optional-header magic is neither PE32 nor PE32+.
        private static int ResolveRvaCountOffset(ushort optionalHeaderMagic)
        {
            if (optionalHeaderMagic == Pe32Magic)
            {
                return Pe32RvaCountOffset;
            }

            if (optionalHeaderMagic == Pe32PlusMagic)
            {
                return Pe32PlusRvaCountOffset;
            }

            return -1;
        }

        // -1 means the optional-header magic is neither PE32 nor PE32+.
        private static int ResolveDataDirectoriesOffset(ushort optionalHeaderMagic)
        {
            if (optionalHeaderMagic == Pe32Magic)
            {
                return Pe32DataDirectoriesOffset;
            }

            if (optionalHeaderMagic == Pe32PlusMagic)
            {
                return Pe32PlusDataDirectoriesOffset;
            }

            return -1;
        }
    }
}
