package clierrors

import "sort"

const maxCommandSuggestions = 5

type commandSuggestionCandidate struct {
	distance int
	name     string
}

// suggestCommands returns the closest available command names for an unknown command.
// Why: listing every available command wastes tokens; a short ranked suggestion list is enough
// to recover from typos like "compil" -> "compile".
func suggestCommands(command string, availableCommands []string) []string {
	if len(availableCommands) == 0 {
		return []string{}
	}

	candidates := make([]commandSuggestionCandidate, 0, len(availableCommands))
	for _, availableCommand := range availableCommands {
		candidates = append(candidates, commandSuggestionCandidate{
			distance: levenshteinDistance(command, availableCommand),
			name:     availableCommand,
		})
	}

	sort.Slice(candidates, func(left int, right int) bool {
		if candidates[left].distance != candidates[right].distance {
			return candidates[left].distance < candidates[right].distance
		}
		return candidates[left].name < candidates[right].name
	})

	limit := maxCommandSuggestions
	if len(candidates) < limit {
		limit = len(candidates)
	}

	suggestions := make([]string, 0, limit)
	for index := 0; index < limit; index++ {
		suggestions = append(suggestions, candidates[index].name)
	}
	return suggestions
}

// levenshteinDistance returns the edit distance between two strings.
func levenshteinDistance(a string, b string) int {
	if a == b {
		return 0
	}
	if len(a) == 0 {
		return len(b)
	}
	if len(b) == 0 {
		return len(a)
	}

	previous := make([]int, len(b)+1)
	current := make([]int, len(b)+1)
	for column := 0; column <= len(b); column++ {
		previous[column] = column
	}

	for row := 1; row <= len(a); row++ {
		current[0] = row
		for column := 1; column <= len(b); column++ {
			deletionCost := previous[column] + 1
			insertionCost := current[column-1] + 1
			substitutionCost := previous[column-1]
			if a[row-1] != b[column-1] {
				substitutionCost++
			}
			current[column] = minInt(deletionCost, insertionCost, substitutionCost)
		}
		previous, current = current, previous
	}

	return previous[len(b)]
}

func minInt(values ...int) int {
	minimum := values[0]
	for _, value := range values[1:] {
		if value < minimum {
			minimum = value
		}
	}
	return minimum
}
