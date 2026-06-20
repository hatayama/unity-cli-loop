#!/bin/sh
set -eu

root=${1:-.}

find "$root" \
  \( -name .git -o -name Library -o -name Temp -o -name node_modules -o -name .agents -o -name .claude -o -name .codex -o -name .cursor -o -name .gemini -o -name .windsurf -o -name .agent \) -prune -o \
  -type f \( -name SKILL.md -o -name '*.md' -o -name '*.sh' -o -name '*.bash' -o -name '*.zsh' -o -name '*.ps1' -o -name '*.psm1' \) -print |
while IFS= read -r file
do
  awk '
    function report(kind) {
      printf "%s %s:%d: %s\n", kind, FILENAME, FNR, $0
    }
    /uloop[[:space:]][^#|;&]*--[[:alnum:]][[:alnum:]-]*([[:space:]]+|=)(true|false)([[:space:]]|$|[|;&])/ {
      report("ARG_BOOL")
    }
    /uloop[[:space:]]+compile[[:space:]][^#|;&]*--(wait-for-domain-reload|reload-external-scene-changes|force-recompile)([[:space:]]|=|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+run-tests[[:space:]][^#|;&]*--save-before-run([[:space:]]|=|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+(record-input|replay-input)[[:space:]][^#|;&]*--show-overlay([[:space:]]|=|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+get-hierarchy[[:space:]][^#|;&]*--include-(components|inactive)([[:space:]]|=|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+execute-dynamic-code[[:space:]][^#|;&]*--compile-only([[:space:]]|=|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /\.(Success|Message|ErrorMessage|ErrorCount|WarningCount|TotalCount|DisplayedCount|LogType|StackTrace|XmlPath|TestCount|PassedCount|FailedCount|SkippedCount|CompletedAt|ScreenshotCount|Screenshots|CompilationErrors|ErrorCode|UpdatedCode|DiagnosticsSummary|OutputPath|InputPath|TotalFrames|DurationSeconds|CurrentFrame|IsReplaying|KeyName|PositionX|PositionY|EndPositionX|EndPositionY|HitGameObjectName|IsPlaying|IsPaused|ClearedLogCount|ClearedCounts)([^[:alnum:]_]|$)/ {
      report("OUTPUT_FIELD")
    }
  ' "$file"
done
