// Test reads the checked-in manifest through a stable relative path during Jest execution.

import { readFileSync } from 'fs';
import { join } from 'path';

type PackageManifest = {
  readonly bin?: Record<string, string>;
};

function loadPackageManifest(): PackageManifest {
  const packageJsonPath = join(__dirname, '..', '..', 'package.json');
  // eslint-disable-next-line security/detect-non-literal-fs-filename
  const packageJsonText = readFileSync(packageJsonPath, 'utf8');
  return JSON.parse(packageJsonText) as PackageManifest;
}

describe('package metadata', () => {
  it('publishes the global uloop command as the dispatcher', () => {
    const packageManifest = loadPackageManifest();

    expect(packageManifest.bin?.['uloop']).toBe('dist/dispatcher.bundle.cjs');
  });

  it('avoids bin target prefixes that npm normalizes during publish', () => {
    const packageManifest = loadPackageManifest();
    const binEntries = Object.entries(packageManifest.bin ?? {});

    expect(binEntries.length).toBeGreaterThan(0);

    for (const [, binTarget] of binEntries) {
      expect(binTarget).not.toMatch(/^(?:\.{1,2}[\\/]|[\\/])/);
    }
  });
});
