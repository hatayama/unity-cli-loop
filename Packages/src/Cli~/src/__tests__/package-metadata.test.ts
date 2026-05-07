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
});
