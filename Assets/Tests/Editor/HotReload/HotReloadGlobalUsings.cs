// Why a dedicated alias: a test-assembly global using applies to every file in this
// assembly, so the name must not collide with existing test identifiers.
// Why sibling csc.rsp -langversion:10: the project default is C# 9, which cannot parse global using.
global using HotReloadGlobalAlias = System.Text.StringBuilder;
