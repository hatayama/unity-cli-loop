using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Regression coverage for <see cref="ManagedAssemblyDetector"/>: the transform worker
    /// reference set must keep native PE images (which share the .dll extension with managed
    /// assemblies on Windows) out of the csc response file.
    /// </summary>
    public class ManagedAssemblyDetectorTests
    {
        private const int DosHeaderSize = 0x40;
        private const int PeSignatureOffsetLocation = 0x3C;
        private const int CoffHeaderStart = DosHeaderSize + 4;
        private const int OptionalHeaderStart = CoffHeaderStart + 20;
        private const int ClrRuntimeHeaderDirectoryIndex = 14;

        /// <summary>
        /// What: a PE32+ image whose CLR runtime header directory has a non-zero RVA is
        /// recognized as a managed assembly.
        /// </summary>
        [Test]
        public void IsManagedAssembly_Pe32PlusWithClrDirectory_ReturnsTrue()
        {
            string filePath = WriteTempFile(
                "managed-pe32plus.dll",
                CreatePeImage(true, 0x2008u, 72u));
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.True);
        }

        /// <summary>
        /// What: a PE32 (32-bit optional header) image with a non-zero CLR runtime header RVA
        /// is recognized as a managed assembly, covering the smaller data-directory layout.
        /// </summary>
        [Test]
        public void IsManagedAssembly_Pe32WithClrDirectory_ReturnsTrue()
        {
            string filePath = WriteTempFile(
                "managed-pe32.dll",
                CreatePeImage(false, 0x2008u, 72u));
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.True);
        }

        /// <summary>
        /// What: a PE32+ image whose CLR runtime header directory RVA is zero (the shape of
        /// native DLLs such as ucrtbase.dll or coreclr.dll) is rejected as not managed.
        /// </summary>
        [Test]
        public void IsManagedAssembly_Pe32PlusWithoutClrDirectory_ReturnsFalse()
        {
            string filePath = WriteTempFile(
                "native-pe32plus.dll",
                CreatePeImage(true, 0u, 0u));
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.False);
        }

        /// <summary>
        /// What: a PE32+ image whose CLR runtime header directory has a non-zero RVA but a
        /// zero size is rejected as malformed instead of being treated as managed.
        /// </summary>
        [Test]
        public void IsManagedAssembly_ClrDirectoryWithZeroSize_ReturnsFalse()
        {
            string filePath = WriteTempFile(
                "zero-size-clr-pe32plus.dll",
                CreatePeImage(true, 0x2008u, 0u));
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.False);
        }

        /// <summary>
        /// What: a file that is large enough to hold a DOS header but does not start with the
        /// 'MZ' magic is rejected as not managed instead of throwing.
        /// </summary>
        [Test]
        public void IsManagedAssembly_NonPeContent_ReturnsFalse()
        {
            byte[] garbage = new byte[256];
            for (int index = 0; index < garbage.Length; index++)
            {
                garbage[index] = (byte)(index % 251);
            }

            string filePath = WriteTempFile("garbage.dll", garbage);
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.False);
        }

        /// <summary>
        /// What: a file shorter than a DOS header is rejected as not managed instead of
        /// throwing on a truncated read.
        /// </summary>
        [Test]
        public void IsManagedAssembly_FileShorterThanDosHeader_ReturnsFalse()
        {
            string filePath = WriteTempFile("truncated.dll", new byte[] { 0x4D, 0x5A, 0x00 });
            Assert.That(ManagedAssemblyDetector.IsManagedAssembly(filePath), Is.False);
        }

        // Builds a minimal PE image: DOS header, PE signature, COFF header, and an optional
        // header with 16 data directories where only the CLR runtime header entry varies.
        private static byte[] CreatePeImage(
            bool isPe32Plus,
            uint clrRuntimeHeaderRva,
            uint clrRuntimeHeaderSize)
        {
            ushort optionalHeaderMagic = isPe32Plus ? (ushort)0x20B : (ushort)0x10B;
            int rvaCountOffset = isPe32Plus ? 108 : 92;
            int dataDirectoriesOffset = isPe32Plus ? 112 : 96;
            ushort optionalHeaderSize = (ushort)(dataDirectoriesOffset + 16 * 8);

            byte[] image = new byte[OptionalHeaderStart + optionalHeaderSize];
            image[0] = 0x4D;
            image[1] = 0x5A;
            WriteInt32(image, PeSignatureOffsetLocation, DosHeaderSize);

            image[DosHeaderSize] = 0x50;
            image[DosHeaderSize + 1] = 0x45;

            // COFF header: machine (AMD64 for PE32+, I386 for PE32), one section,
            // SizeOfOptionalHeader, characteristics.
            WriteUInt16(
                image,
                CoffHeaderStart,
                isPe32Plus ? (ushort)0x8664 : (ushort)0x14C);
            WriteUInt16(image, CoffHeaderStart + 2, 1);
            WriteUInt16(image, CoffHeaderStart + 16, optionalHeaderSize);
            WriteUInt16(image, CoffHeaderStart + 18, 0x2022);

            WriteUInt16(image, OptionalHeaderStart, optionalHeaderMagic);
            WriteInt32(image, OptionalHeaderStart + rvaCountOffset, 16);

            int clrDirectoryOffset =
                OptionalHeaderStart + dataDirectoriesOffset + ClrRuntimeHeaderDirectoryIndex * 8;
            WriteInt32(image, clrDirectoryOffset, (int)clrRuntimeHeaderRva);
            WriteInt32(image, clrDirectoryOffset + 4, (int)clrRuntimeHeaderSize);

            return image;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static string WriteTempFile(string fileName, byte[] content)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workRootPath = Path.Combine(
                projectRootPath, "Library", "UloopHotReloadTests", "ManagedAssemblyDetector");
            Directory.CreateDirectory(workRootPath);
            string filePath = Path.Combine(workRootPath, fileName);
            File.WriteAllBytes(filePath, content);
            return filePath;
        }
    }
}
