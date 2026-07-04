package clicore

import "github.com/hatayama/unity-cli-loop/common/skillscan"

const (
	SkillFileName       = skillscan.SkillFileName
	SkillSearchMaxDepth = skillscan.SkillSearchMaxDepth
)

var ExcludedSkillSearchDirs = skillscan.ExcludedSkillSearchDirs

type SkillSourceRoot = skillscan.SkillSourceRoot

func collectInternalSkillToolNames(projectRoot string) map[string]bool {
	return skillscan.CollectInternalSkillToolNames(projectRoot)
}

func FindEditorFolders(basePath string, maxDepth int) []string {
	return skillscan.FindEditorFolders(basePath, maxDepth)
}

func ResolvePackageRoot(projectRoot string) string {
	return skillscan.ResolvePackageRoot(projectRoot)
}

func EnumerateSkillSourceRoots(projectRoot string) []SkillSourceRoot {
	return skillscan.EnumerateSkillSourceRoots(projectRoot)
}

func FallbackSkillName(skillDirectory string) string {
	return skillscan.FallbackSkillName(skillDirectory)
}

func ParseSkillFrontmatter(content string) map[string]string {
	return skillscan.ParseSkillFrontmatter(content)
}
