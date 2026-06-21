#!/bin/sh
set -eu

root=${1:-.}

find "$root" \
  \( -name .git -o -name Library -o -name Temp -o -name node_modules -o -name .agents -o -name .claude -o -name .codex -o -name .cursor -o -name .gemini -o -name .windsurf -o -name .agent -o -path '*/TemporarySkills~/v3-cli-invocation-migration' \) -prune -o \
  -type f \( -name SKILL.md -o -name '*.md' -o -name '*.sh' -o -name '*.bash' -o -name '*.zsh' -o -name '*.ps1' -o -name '*.psm1' \) -print |
while IFS= read -r file
do
  awk '
    BEGIN {
      output_fields = "Success|Message|ErrorMessage|ErrorCount|WarningCount|TotalCount|DisplayedCount|LogType|StackTrace|XmlPath|TestCount|PassedCount|FailedCount|SkippedCount|CompletedAt|ScreenshotCount|Screenshots|CompilationErrors|ErrorCode|UpdatedCode|DiagnosticsSummary|OutputPath|InputPath|TotalFrames|DurationSeconds|CurrentFrame|IsReplaying|KeyName|PositionX|PositionY|EndPositionX|EndPositionY|HitGameObjectName|IsPlaying|IsPaused|ClearedLogCount|ClearedCounts"
    }
    function report(kind) {
      printf "%s %s:%d: %s\n", kind, FILENAME, FNR, $0
    }
    function update_json_variables(line, rest, assignment, variable_name) {
      rest = line
      while (match(rest, /\$[A-Za-z_][A-Za-z0-9_]*[[:space:]]*=[^\n#;]*/)) {
        assignment = substr(rest, RSTART, RLENGTH)
        variable_name = assignment
        sub(/^\$/, "", variable_name)
        sub(/[[:space:]]*=.*/, "", variable_name)
        if (assignment ~ /ConvertFrom-Json/) {
          json_variables[variable_name] = 1
        } else {
          delete json_variables[variable_name]
        }
        rest = substr(rest, RSTART + RLENGTH)
      }
    }
    function contains_jq_output_field(line) {
      return line ~ /(^|[[:space:]`|;&(])jq[[:space:]]/ &&
        line ~ ("\\.(" output_fields ")([^[:alnum:]_]|$)")
    }
    function contains_powershell_json_output_field(line, variable_name, pattern) {
      for (variable_name in json_variables) {
        pattern = "\\$" variable_name "\\.(" output_fields ")([^[:alnum:]_]|$)"
        if (line ~ pattern) {
          return 1
        }
      }

      return 0
    }
    /uloop[[:space:]][^#|;&]*--[[:alnum:]][[:alnum:]-]*([[:space:]]+|=)(true|false)([^[:alnum:]_-]|$)/ {
      report("ARG_BOOL")
    }
    /uloop[[:space:]]+compile[[:space:]][^#|;&]*--(wait-for-domain-reload|reload-external-scene-changes|force-recompile)([^[:alnum:]_-]|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+run-tests[[:space:]][^#|;&]*--save-before-run([^[:alnum:]_-]|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+(record-input|replay-input)[[:space:]][^#|;&]*--show-overlay([^[:alnum:]_-]|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+get-hierarchy[[:space:]][^#|;&]*--include-(components|inactive)([^[:alnum:]_-]|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    /uloop[[:space:]]+execute-dynamic-code[[:space:]][^#|;&]*--compile-only([^[:alnum:]_-]|$)/ {
      report("FIRST_PARTY_OPTION")
    }
    {
      update_json_variables($0)
    }
    contains_jq_output_field($0) || contains_powershell_json_output_field($0) {
      report("OUTPUT_FIELD")
    }
  ' "$file"
done
