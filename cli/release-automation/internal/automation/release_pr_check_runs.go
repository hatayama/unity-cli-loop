package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"strconv"
	"time"
)

type releaseWorkflowRun struct {
	DatabaseID int64  `json:"databaseId"`
	HeadSHA    string `json:"headSha"`
	CreatedAt  string `json:"createdAt"`
}

func findDispatchedReleasePRCheckRun(
	ctx context.Context,
	config releasePRCheckConfig,
	workflow string,
	releasePR releasePullRequest,
	dispatchedAt time.Time,
	deps releasePRCheckDeps,
) (releaseWorkflowRun, error) {
	for attempt := 0; attempt < config.lookupAttempts; attempt++ {
		run, found, err := latestReleasePRCheckRun(ctx, config, workflow, releasePR, dispatchedAt, deps)
		if err != nil {
			return releaseWorkflowRun{}, err
		}
		if found {
			return run, nil
		}
		if attempt+1 < config.lookupAttempts {
			err = deps.sleep(ctx, time.Duration(config.lookupIntervalSeconds)*time.Second)
			if err != nil {
				return releaseWorkflowRun{}, err
			}
		}
	}
	return releaseWorkflowRun{}, fmt.Errorf("could not find dispatched %s workflow run for %s", workflow, releasePR.HeadRefOID)
}

func latestReleasePRCheckRun(
	ctx context.Context,
	config releasePRCheckConfig,
	workflow string,
	releasePR releasePullRequest,
	dispatchedAt time.Time,
	deps releasePRCheckDeps,
) (releaseWorkflowRun, bool, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"run",
		"list",
		"--repo",
		config.repository,
		"--workflow",
		workflow,
		"--branch",
		releasePR.HeadRefName,
		"--event",
		"workflow_dispatch",
		"--json",
		"databaseId,status,conclusion,headSha,createdAt,url",
		"--limit",
		"20",
	)
	if err != nil {
		return releaseWorkflowRun{}, false, err
	}

	runs := []releaseWorkflowRun{}
	err = json.Unmarshal([]byte(output), &runs)
	if err != nil {
		return releaseWorkflowRun{}, false, fmt.Errorf("failed to parse workflow runs: %w", err)
	}

	var latestRun releaseWorkflowRun
	var latestCreatedAt time.Time
	found := false
	for _, run := range runs {
		createdAt, err := time.Parse(time.RFC3339, run.CreatedAt)
		if err != nil {
			return releaseWorkflowRun{}, false, fmt.Errorf("failed to parse workflow run createdAt %q: %w", run.CreatedAt, err)
		}
		if createdAt.Before(dispatchedAt) {
			continue
		}
		if !found || createdAt.After(latestCreatedAt) {
			latestRun = run
			latestCreatedAt = createdAt
			found = true
		}
	}
	if !found {
		return releaseWorkflowRun{}, false, nil
	}
	return latestRun, true, nil
}

func watchReleasePRCheckRun(ctx context.Context, config releasePRCheckConfig, runID int64, deps releasePRCheckDeps) error {
	_, err := deps.runOutput(
		ctx,
		"gh",
		"run",
		"watch",
		strconv.FormatInt(runID, 10),
		"--repo",
		config.repository,
		"--exit-status",
		"--compact",
		"--interval",
		strconv.Itoa(config.watchIntervalSeconds),
	)
	return err
}
