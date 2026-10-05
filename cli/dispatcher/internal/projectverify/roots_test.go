package projectverify_test

import (
	"path/filepath"
	"testing"
)

// Verifies a project without Packages/ scans Assets only and reports the missing manifest.
func TestRunReportsMissingManifest(t *testing.T) {
	root := writeProject(t, nil)

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, "Packages/manifest.json does not exist. Unity will recreate it with the"+
		" default packages only, dropping the packages this project uses. Restore it from version control.")
}

// Verifies a manifest without a dependencies key is a project without dependencies, not a problem.
func TestRunAcceptsManifestWithoutDependencies(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": "{}"})

	assertFindings(t, runProject(t, root))
}

// Verifies a manifest that is not JSON is reported with the full message.
func TestRunReportsManifestThatIsNotJSON(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": `{"dependencies":`})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report,
		"Packages/manifest.json is not valid JSON. Unity cannot resolve packages until it is fixed.")
}

// Verifies a manifest whose top level is an array is reported.
func TestRunReportsManifestThatIsNotAnObject(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": "[]"})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, "Packages/manifest.json must contain a JSON object.")
}

// Verifies a manifest that is JSON null is reported, since null decodes into a nil map without an error.
func TestRunReportsNullManifest(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": "null"})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, "Packages/manifest.json must contain a JSON object.")
}

// Verifies dependencies that are an array are reported.
func TestRunReportsDependenciesThatAreNotAnObject(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": `{"dependencies":[]}`})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, `"dependencies" in Packages/manifest.json must be a JSON object.`)
}

// Verifies dependencies that are JSON null are reported rather than read as no dependencies.
func TestRunReportsNullDependencies(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": `{"dependencies":null}`})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, `"dependencies" in Packages/manifest.json must be a JSON object.`)
}

// Verifies a number and a null dependency value are each reported while the local package listed
// between them is still scanned.
func TestRunReportsNonStringDependencyAndKeepsCheckingOthers(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"a.num":   1,
		"b.local": "file:../Local",
		"c.null":  nil,
	}))
	writePackage(t, filepath.Join(root, "Local"), map[string]string{"Runtime.cs": "class Runtime {}"})

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid(), manifestInvalid(), metaMissing("Local/Runtime.cs"))
	assertManifestMessages(t, report,
		"Dependency a.num in Packages/manifest.json must have a string value.",
		"Dependency c.null in Packages/manifest.json must have a string value.")
}

// Verifies a local package outside the project is scanned and named by its absolute path.
func TestRunScansLocalFilePackageOutsideProject(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.outside": "file:../../OutsidePkg"}))
	packageDir := filepath.Join(filepath.Dir(root), "OutsidePkg")
	writePackage(t, packageDir, map[string]string{"Runtime.cs": "class Runtime {}"})

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", filepath.ToSlash(packageDir))
	assertFindings(t, report, metaMissing(filepath.ToSlash(packageDir)+"/Runtime.cs"))
}

// Verifies a local package folder without package.json is reported and not scanned.
func TestRunReportsLocalPackageWithoutPackageJSON(t *testing.T) {
	root := writeProject(t, map[string]string{"NoPackageJson/Runtime.cs": "class Runtime {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.nojson": "file:../NoPackageJson"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, "Dependency com.example.nojson in Packages/manifest.json points to"+
		" ../NoPackageJson, which has no package.json. Unity cannot load it as a package.")
}

// Verifies a local package path that does not exist is reported with the full message.
func TestRunReportsMissingLocalPackage(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.gone": "file:../NoSuchPackage"}))

	report := runProject(t, root)

	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report, "Dependency com.example.gone in Packages/manifest.json points to"+
		" ../NoSuchPackage, which does not exist. Unity cannot resolve the package, so the project opens"+
		" with errors. Fix the path or remove the dependency.")
}

// Verifies a local tarball package is accepted without becoming a scanned root.
func TestRunSkipsLocalTarballPackage(t *testing.T) {
	root := writeProject(t, map[string]string{"pkg.tgz": "not a real archive"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.tarball": "file:../pkg.tgz"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies an empty file: path is reported.
func TestRunReportsEmptyLocalPackagePath(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.empty": "file:"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report, manifestInvalid())
	assertManifestMessages(t, report,
		"Dependency com.example.empty in Packages/manifest.json has an empty file: path.")
}

// Verifies an absolute file: path becomes a scanned root.
func TestRunResolvesAbsoluteLocalPackagePath(t *testing.T) {
	packageDir := filepath.Join(t.TempDir(), "AbsolutePkg")
	writePackage(t, packageDir, nil)
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.absolute": "file:" + filepath.ToSlash(packageDir)}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", filepath.ToSlash(packageDir))
	assertFindings(t, report)
}

// Verifies a local package inside the project but outside Assets and Packages is scanned and named
// by its project-relative path.
func TestRunScansLocalPackageInsideProjectOutsideAssets(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.local": "file:../LocalPackages/com.local"}))
	writePackage(t, filepath.Join(root, "LocalPackages", "com.local"),
		map[string]string{"Runtime.cs": "class Runtime {}"})

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "LocalPackages/com.local")
	assertFindings(t, report, metaMissing("LocalPackages/com.local/Runtime.cs"))
}

// Verifies a local package inside Assets is scanned once, so its .meta files are not reported as
// duplicates of themselves.
func TestRunDoesNotScanLocalPackageNestedInAssetsTwice(t *testing.T) {
	root := writeProject(t, map[string]string{
		"Assets/Vendor.meta":       metaText(guidOf(1)),
		"Assets/Vendor/a.txt":      "a",
		"Assets/Vendor/a.txt.meta": metaText(guidOf(2)),
	})
	writePackage(t, filepath.Join(root, "Assets", "Vendor"), nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.vendor": "file:../Assets/Vendor"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies two dependencies on the same folder scan it once.
func TestRunScansSharedLocalPackageOnce(t *testing.T) {
	root := writeProject(t, nil)
	packageDir := filepath.Join(filepath.Dir(root), "Shared")
	writePackage(t, packageDir, map[string]string{"a.txt": "a", "a.txt.meta": metaText(guidOf(1))})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.one": "file:../../Shared",
		"com.example.two": "file:../../Shared/",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", filepath.ToSlash(packageDir))
	assertFindings(t, report)
}

// Verifies a local dependency on an embedded package folder scans that folder once.
func TestRunScansLocalPackagePointingAtEmbeddedPackageOnce(t *testing.T) {
	root := writeProject(t, nil)
	writePackage(t, filepath.Join(root, "Packages", "com.example.embedded"),
		map[string]string{"a.txt": "a", "a.txt.meta": metaText(guidOf(1))})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.embedded": "file:com.example.embedded"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Packages/com.example.embedded")
	assertFindings(t, report)
}

// Verifies a local package inside a hidden folder of an embedded package is scanned as its own
// root, since the embedded package's scan never enters the hidden folder.
func TestRunScansLocalPackageBelowHiddenFolderOfEmbeddedPackage(t *testing.T) {
	root := writeProject(t, nil)
	embeddedDir := filepath.Join(root, "Packages", "com.example.embedded")
	writePackage(t, embeddedDir, nil)
	writePackage(t, filepath.Join(embeddedDir, "Sub~", "com.example.inner"),
		map[string]string{"NoMeta.cs": "class NoMeta {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.inner": "file:com.example.embedded/Sub~/com.example.inner",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report,
		"Assets", "Packages/com.example.embedded", "Packages/com.example.embedded/Sub~/com.example.inner")
	assertFindings(t, report, metaMissing("Packages/com.example.embedded/Sub~/com.example.inner/NoMeta.cs"))
}

// Verifies a local package inside a plugin folder of Assets is scanned as its own root, since the
// Assets scan treats the plugin folder as one asset and never enters it.
func TestRunScansLocalPackageInsidePluginFolder(t *testing.T) {
	root := writeProject(t, map[string]string{"Assets/Tool.bundle.meta": metaText(guidOf(1))})
	writePackage(t, filepath.Join(root, "Assets", "Tool.bundle", "com.example.inner"),
		map[string]string{"NoMeta.cs": "class NoMeta {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.inner": "file:../Assets/Tool.bundle/com.example.inner",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Assets/Tool.bundle/com.example.inner")
	assertFindings(t, report, metaMissing("Assets/Tool.bundle/com.example.inner/NoMeta.cs"))
}

// Verifies a local package reached through a linked folder in Assets is scanned as its own root,
// since the Assets scan does not follow the link.
func TestRunScansLocalPackageBehindSymlinkInsideAssets(t *testing.T) {
	root := writeProject(t, map[string]string{"Assets/Link.meta": metaText(guidOf(1))})
	linkedDir := filepath.Join(filepath.Dir(root), "LinkedPackages")
	writePackage(t, filepath.Join(linkedDir, "com.example.linked"),
		map[string]string{"NoMeta.cs": "class NoMeta {}"})
	makeSymlink(t, linkedDir, filepath.Join(root, "Assets", "Link"))
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.linked": "file:../Assets/Link/com.example.linked",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Assets/Link/com.example.linked")
	assertFindings(t, report, metaMissing("Assets/Link/com.example.linked/NoMeta.cs"))
}

// Verifies a local package whose own folder in Assets has a hidden name is scanned as its own
// root, since the Assets scan skips hidden folders.
func TestRunScansLocalPackageWhoseFolderIsHidden(t *testing.T) {
	root := writeProject(t, nil)
	writePackage(t, filepath.Join(root, "Assets", "Inner~"), map[string]string{"NoMeta.cs": "class NoMeta {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.inner": "file:../Assets/Inner~",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Assets/Inner~")
	assertFindings(t, report, metaMissing("Assets/Inner~/NoMeta.cs"))
}

// Verifies a file: path to the project itself or to a folder containing it is reported and not scanned.
func TestRunReportsLocalPackageThatContainsProject(t *testing.T) {
	root := writeProject(t, nil)
	writePackage(t, root, nil)
	writePackage(t, filepath.Dir(root), nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.self":   "file:..",
		"com.example.parent": "file:../..",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report, manifestInvalid(), manifestInvalid())
	assertManifestMessages(t, report,
		"Dependency com.example.parent in Packages/manifest.json points to ../.., which is this project"+
			" or contains it. Point it at the package folder instead.",
		"Dependency com.example.self in Packages/manifest.json points to .., which is this project"+
			" or contains it. Point it at the package folder instead.")
}

// Verifies a local package that cannot be inspected stops the run instead of being reported missing.
func TestRunFailsWhenLocalPackageCannotBeInspected(t *testing.T) {
	root := writeProject(t, nil)
	lockedDir := filepath.Join(filepath.Dir(root), "Locked")
	packageDir := filepath.Join(lockedDir, "Pkg")
	writePackage(t, packageDir, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.locked": "file:../../Locked/Pkg"}))
	lockDirectory(t, lockedDir, 0)

	assertRunFails(t, root, packageDir)
}

// Verifies a Packages folder that cannot be listed stops the run instead of silently skipping
// every embedded package. The folder stays searchable, so the manifest below it still reads and
// only the listing fails.
func TestRunFailsWhenPackagesCannotBeRead(t *testing.T) {
	root := writeProjectWithManifest(t, nil)
	packagesDir := filepath.Join(root, "Packages")
	lockDirectory(t, packagesDir, searchOnlyMode)

	assertRunFails(t, root, packagesDir)
}

// Verifies registry and git dependencies are neither checked nor scanned.
func TestRunIgnoresRegistryAndGitDependencies(t *testing.T) {
	root := writeProject(t, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.unity.ugui":  "1.0.0",
		"com.example.git": "https://example.invalid/repo.git",
	}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies a manifest that starts with a UTF-8 byte order mark is read as JSON.
func TestRunReadsManifestWithByteOrderMark(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json": "\xEF\xBB\xBF" + `{"dependencies":{}}`})

	assertFindings(t, runProject(t, root))
}

// Verifies an embedded package is scanned and its findings are named under Packages/.
func TestRunScansEmbeddedPackage(t *testing.T) {
	root := writeProjectWithManifest(t, nil)
	writePackage(t, filepath.Join(root, "Packages", "com.example.embedded"),
		map[string]string{"Editor.cs": "class Editor {}"})

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Packages/com.example.embedded")
	assertFindings(t, report, metaMissing("Packages/com.example.embedded/Editor.cs"))
}

// Verifies folders under Packages/ without a package.json file are not scanned.
func TestRunIgnoresPackagesFolderWithoutPackageJSON(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Packages/NoJson/Runtime.cs":     "class Runtime {}",
		"Packages/DirJson/package.json/": "",
		"Packages/DirJson/Runtime.cs":    "class Runtime {}",
	})

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies hidden folders under Packages/ are not scanned even with a package.json file.
func TestRunIgnoresHiddenPackagesFolder(t *testing.T) {
	root := writeProjectWithManifest(t, nil)
	writePackage(t, filepath.Join(root, "Packages", ".cache"), map[string]string{"Runtime.cs": "class Runtime {}"})
	writePackage(t, filepath.Join(root, "Packages", "Old~"), map[string]string{"Runtime.cs": "class Runtime {}"})

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies a symbolic link under Packages/ is not followed into a scanned root.
func TestRunDoesNotFollowSymlinkedEmbeddedPackage(t *testing.T) {
	root := writeProjectWithManifest(t, nil)
	targetDir := filepath.Join(filepath.Dir(root), "LinkTarget")
	writePackage(t, targetDir, map[string]string{"Runtime.cs": "class Runtime {}"})
	makeSymlink(t, targetDir, filepath.Join(root, "Packages", "Linked"))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets")
	assertFindings(t, report)
}

// Verifies ScannedRoots lists Assets, then embedded packages by name, then local packages.
func TestRunListsScannedRootsInOrder(t *testing.T) {
	root := writeProject(t, nil)
	writePackage(t, filepath.Join(root, "Packages", "b.pkg"), nil)
	writePackage(t, filepath.Join(root, "Packages", "a.pkg"), nil)
	localDir := filepath.Join(filepath.Dir(root), "Local")
	writePackage(t, localDir, nil)
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.local": "file:../../Local"}))

	report := runProject(t, root)

	assertScannedRoots(t, report, "Assets", "Packages/a.pkg", "Packages/b.pkg", filepath.ToSlash(localDir))
	assertFindings(t, report)
}

// Verifies a manifest path that is a directory stops the run instead of being reported missing.
func TestRunFailsWhenManifestIsADirectory(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages/manifest.json/": ""})

	assertRunFails(t, root, filepath.Join(root, "Packages", "manifest.json"))
}

// Verifies a Packages entry that is a file stops the run.
func TestRunFailsWhenPackagesIsAFile(t *testing.T) {
	root := writeProject(t, map[string]string{"Packages": ""})

	assertRunFails(t, root, filepath.Join(root, "Packages")+" is not a directory")
}
