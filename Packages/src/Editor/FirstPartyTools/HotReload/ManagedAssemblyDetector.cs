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

            if (stream.Length < DosHeaderSize)
            {
                return false;
            }

            // DOS header magic 'M','Z'.
            if (reader.ReadByte() != 0x4D || reader.ReadByte() != 0x5A)
            {
                return false;
            }

            stream.Position = PeSignatureOffsetLocation;
            int peSignatureOffset = reader.ReadInt32();
            if (peSignatureOffset < DosHeaderSize
                || peSignatureOffset > stream.Length - PeSignatureSize - CoffHeaderSize)
            {
                return false;
            }

            // PE signature 'P','E','\0','\0'.
            stream.Position = peSignatureOffset;
            if (reader.ReadByte() != 0x50 || reader.ReadByte() != 0x45
                || reader.ReadByte() != 0 || reader.ReadByte() != 0)
            {
                return false;
            }

            long coffHeaderStart = peSignatureOffset + PeSignatureSize;
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
            int rvaCountOffset;
            int dataDirectoriesOffset;
            if (optionalHeaderMagic == Pe32Magic)
            {
                rvaCountOffset = Pe32RvaCountOffset;
                dataDirectoriesOffset = Pe32DataDirectoriesOffset;
            }
            else if (optionalHeaderMagic == Pe32PlusMagic)
            {
                rvaCountOffset = Pe32PlusRvaCountOffset;
                dataDirectoriesOffset = Pe32PlusDataDirectoriesOffset;
            }
            else
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
            return clrRuntimeHeaderRva != 0;
        }
    }
}
