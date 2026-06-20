// Test reads the checked-in manifest through a stable relative path during Jest execution.

import { readFileSync } from 'fs';
import { join } from 'path';

type PackageManifest = {
  readonly bin?: Record<string, string>;
};

type UnityPackageManifest = {
  readonly dependencies?: Record<string, string>;
};

type AssemblyVersionDefine = {
  readonly name?: string;
  readonly expression?: string;
  readonly define?: string;
};

type AssemblyDefinition = {
  readonly versionDefines?: readonly AssemblyVersionDefine[];
};

const TEST_FRAMEWORK_PACKAGE_NAME = 'com.unity.test-framework';
const TEST_FRAMEWORK_DEFINE = 'ULOOPMCP_HAS_TEST_FRAMEWORK';
const METADATA_VALIDATION_DEPENDENCY_META_PATHS = [
  'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Collections.Immutable.dll.meta',
  'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Reflection.Metadata.dll.meta',
  'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Runtime.CompilerServices.Unsafe.dll.meta',
] as const;
const METADATA_VALIDATION_DEPENDENCY_DEFINE_CONSTRAINT = '!UNITY_6000_5_OR_NEWER';
const METADATA_VALIDATION_PRIVATE_ASSEMBLIES = [
  {
    relativePath:
      'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Collections.Immutable.dll',
    assemblyName: 'uLoopMCP.System.Collections.Immutable',
    assemblyReferences: ['uLoopMCP.System.Runtime.CompilerServices.Unsafe'],
  },
  {
    relativePath: 'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Reflection.Metadata.dll',
    assemblyName: 'uLoopMCP.System.Reflection.Metadata',
    assemblyReferences: [
      'uLoopMCP.System.Collections.Immutable',
      'uLoopMCP.System.Runtime.CompilerServices.Unsafe',
    ],
  },
  {
    relativePath:
      'Editor/MetadataValidation/Dependencies/uLoopMCP.System.Runtime.CompilerServices.Unsafe.dll',
    assemblyName: 'uLoopMCP.System.Runtime.CompilerServices.Unsafe',
    assemblyReferences: [],
  },
] as const;

// CoreCLR rejects assemblies whose COR20 header claims StrongNameSigned while the
// signature and public key were stripped during the identity rewrite ("Bad IL format").
const COMIMAGE_FLAGS_STRONGNAMESIGNED = 0x8;
const CLI_HEADER_DATA_DIRECTORY_INDEX = 14;

// Reads the Flags field of the COR20 (CLI) header from a managed PE image.
function readCorHeaderFlags(dllBytes: Buffer): number {
  const peSignatureOffset = dllBytes.readUInt32LE(0x3c);
  const coffHeaderOffset = peSignatureOffset + 4;
  const numberOfSections = dllBytes.readUInt16LE(coffHeaderOffset + 2);
  const sizeOfOptionalHeader = dllBytes.readUInt16LE(coffHeaderOffset + 16);
  const optionalHeaderOffset = coffHeaderOffset + 20;
  const isPe32Plus = dllBytes.readUInt16LE(optionalHeaderOffset) === 0x20b;
  const dataDirectoriesOffset = optionalHeaderOffset + (isPe32Plus ? 112 : 96);
  const cliHeaderRva = dllBytes.readUInt32LE(
    dataDirectoriesOffset + CLI_HEADER_DATA_DIRECTORY_INDEX * 8,
  );

  const sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
  for (let sectionIndex = 0; sectionIndex < numberOfSections; sectionIndex++) {
    const sectionOffset = sectionTableOffset + sectionIndex * 40;
    const virtualSize = dllBytes.readUInt32LE(sectionOffset + 8);
    const virtualAddress = dllBytes.readUInt32LE(sectionOffset + 12);
    const pointerToRawData = dllBytes.readUInt32LE(sectionOffset + 20);
    if (cliHeaderRva >= virtualAddress && cliHeaderRva < virtualAddress + virtualSize) {
      const cliHeaderOffset = cliHeaderRva - virtualAddress + pointerToRawData;
      return dllBytes.readUInt32LE(cliHeaderOffset + 16);
    }
  }

  throw new Error('CLI header RVA does not fall inside any PE section');
}

function loadPackageManifest(): PackageManifest {
  const packageJsonPath = join(__dirname, '..', '..', 'package.json');
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  const packageJsonText = readFileSync(packageJsonPath, 'utf8');
  return JSON.parse(packageJsonText) as PackageManifest;
}

function loadUnityPackageManifest(): UnityPackageManifest {
  const packageJsonPath = join(__dirname, '..', '..', '..', 'package.json');
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  const packageJsonText = readFileSync(packageJsonPath, 'utf8');
  return JSON.parse(packageJsonText) as UnityPackageManifest;
}

function loadEditorAssemblyDefinition(): AssemblyDefinition {
  const asmdefPath = join(__dirname, '..', '..', '..', 'Editor', 'uLoopMCP.Editor.asmdef');
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  const asmdefText = readFileSync(asmdefPath, 'utf8');
  return JSON.parse(asmdefText) as AssemblyDefinition;
}

function loadUnityPackageText(relativePath: string): string {
  const assetPath = join(__dirname, '..', '..', '..', relativePath);
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  return readFileSync(assetPath, 'utf8');
}

function loadUnityPackageBytes(relativePath: string): Buffer {
  const assetPath = join(__dirname, '..', '..', '..', relativePath);
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  return readFileSync(assetPath);
}

describe('package metadata', () => {
  it('avoids bin target prefixes that npm normalizes during publish', () => {
    const packageManifest = loadPackageManifest();
    const binEntries = Object.entries(packageManifest.bin ?? {});

    expect(binEntries.length).toBeGreaterThan(0);

    for (const [, binTarget] of binEntries) {
      expect(binTarget).not.toMatch(/^(?:\.{1,2}[\\/]|[\\/])/);
    }
  });

  it('does not force Unity Test Framework into consuming projects', () => {
    const packageManifest = loadUnityPackageManifest();

    expect(Object.keys(packageManifest.dependencies ?? {})).not.toContain(
      TEST_FRAMEWORK_PACKAGE_NAME,
    );
  });

  it('defines a compile symbol when Unity Test Framework is installed', () => {
    const assemblyDefinition = loadEditorAssemblyDefinition();

    expect(assemblyDefinition.versionDefines ?? []).toContainEqual(
      expect.objectContaining({
        name: TEST_FRAMEWORK_PACKAGE_NAME,
        define: TEST_FRAMEWORK_DEFINE,
      }),
    );
  });

  it('keeps bundled metadata validation dependencies out of implicit project references', () => {
    for (const metaPath of METADATA_VALIDATION_DEPENDENCY_META_PATHS) {
      const metaText = loadUnityPackageText(metaPath);

      expect(metaText).toContain('isExplicitlyReferenced: 1');
    }
  });

  it('excludes bundled metadata validation dependencies from Unity 6000.5 and newer', () => {
    for (const metaPath of METADATA_VALIDATION_DEPENDENCY_META_PATHS) {
      const metaText = loadUnityPackageText(metaPath);

      expect(metaText).toContain(`- '${METADATA_VALIDATION_DEPENDENCY_DEFINE_CONSTRAINT}'`);
    }
  });

  it('uses private assembly identities for metadata validation dependencies', () => {
    for (const assembly of METADATA_VALIDATION_PRIVATE_ASSEMBLIES) {
      const dllBytes = loadUnityPackageBytes(assembly.relativePath);

      expect(dllBytes.includes(Buffer.from(assembly.assemblyName))).toBe(true);
      for (const assemblyReference of assembly.assemblyReferences) {
        expect(dllBytes.includes(Buffer.from(assemblyReference))).toBe(true);
      }
    }
  });

  it('does not claim strong-name signing on unsigned metadata validation dependencies', () => {
    for (const assembly of METADATA_VALIDATION_PRIVATE_ASSEMBLIES) {
      const dllBytes = loadUnityPackageBytes(assembly.relativePath);

      expect(readCorHeaderFlags(dllBytes) & COMIMAGE_FLAGS_STRONGNAMESIGNED).toBe(0);
    }
  });

  it('does not keep orphaned plugin folder metadata', () => {
    expect(() => loadUnityPackageText('Plugins.meta')).toThrow();
  });
});
