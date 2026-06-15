package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"strings"
)

type protocolMinimumVersionCommentConfig struct {
	repositoryRoot string
	pullRequest    string
	repository     string
	baseRef        string
	headRef        string
}

func RunProtocolMinimumVersionComment(ctx context.Context, stdout io.Writer, stderr io.Writer) int {
	config, err := protocolMinimumVersionCommentConfigFromEnvironment()
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}
	if config.pullRequest == "" {
		writeProtocolMinimumVersionLine(stdout, "Skipping protocol minimum version comment because no PR number was provided.")
		return 0
	}
	if config.baseRef == "" {
		writeProtocolMinimumVersionLine(stdout, "Skipping protocol minimum version comment because no base ref was provided.")
		return 0
	}

	result, err := AnalyzeProtocolMinimumVersionGuardForRefs(ctx, ProtocolMinimumVersionGuardConfig{
		BaseRef: config.baseRef,
		HeadRef: config.headRef,
	})
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}

	repository, err := resolveProtocolMinimumVersionRepository(ctx, config)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}
	config.repository = repository

	if result.NeedsMinimumVersionUpdate {
		message, err := upsertProtocolMinimumVersionComment(ctx, config, FormatProtocolMinimumVersionWarning(result))
		if err != nil {
			writeProtocolMinimumVersionLine(stderr, err)
			return 1
		}
		writeProtocolMinimumVersionLine(stdout, message)
		return 0
	}

	deleted, err := deleteProtocolMinimumVersionComment(ctx, config)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}
	if deleted {
		writeProtocolMinimumVersionLine(stdout, "Deleted resolved protocol minimum version comment.")
	}
	return 0
}

func protocolMinimumVersionCommentConfigFromEnvironment() (protocolMinimumVersionCommentConfig, error) {
	repositoryRoot := os.Getenv("ULOOP_REPOSITORY_ROOT")
	if repositoryRoot == "" {
		workingDirectory, err := os.Getwd()
		if err != nil {
			return protocolMinimumVersionCommentConfig{}, fmt.Errorf("failed to resolve repository root: %w", err)
		}
		repositoryRoot = workingDirectory
	}

	baseRef := os.Getenv("PROTOCOL_MINIMUM_VERSION_BASE_REF")
	if baseRef == "" && os.Getenv("GITHUB_BASE_REF") != "" {
		baseRef = "origin/" + os.Getenv("GITHUB_BASE_REF")
	}

	headRef := os.Getenv("PROTOCOL_MINIMUM_VERSION_HEAD_REF")
	if headRef == "" {
		headRef = "HEAD"
	}

	return protocolMinimumVersionCommentConfig{
		repositoryRoot: repositoryRoot,
		pullRequest:    os.Getenv("PR_NUMBER"),
		repository:     os.Getenv("GITHUB_REPOSITORY"),
		baseRef:        baseRef,
		headRef:        headRef,
	}, nil
}

func resolveProtocolMinimumVersionRepository(
	ctx context.Context,
	config protocolMinimumVersionCommentConfig,
) (string, error) {
	if config.repository != "" {
		return config.repository, nil
	}
	output, err := runProtocolMinimumVersionOutput(
		ctx,
		config.repositoryRoot,
		"gh",
		"repo",
		"view",
		"--json",
		"nameWithOwner",
		"--jq",
		".nameWithOwner")
	if err != nil {
		return "", err
	}
	return strings.TrimSpace(output), nil
}

func upsertProtocolMinimumVersionComment(
	ctx context.Context,
	config protocolMinimumVersionCommentConfig,
	body string,
) (string, error) {
	commentID, err := existingProtocolMinimumVersionComment(ctx, config)
	if err != nil {
		return "", err
	}

	bodyFile, cleanup, err := writeProtocolMinimumVersionBodyFile(body)
	if err != nil {
		return "", err
	}
	defer cleanup()

	if commentID != "" {
		_, err = runProtocolMinimumVersionOutput(
			ctx,
			config.repositoryRoot,
			"gh",
			"api",
			"--method",
			"PATCH",
			"repos/"+config.repository+"/issues/comments/"+commentID,
			"--input",
			bodyFile)
		return "Updated protocol minimum version comment.", err
	}

	_, err = runProtocolMinimumVersionOutput(
		ctx,
		config.repositoryRoot,
		"gh",
		"api",
		"--method",
		"POST",
		"repos/"+config.repository+"/issues/"+config.pullRequest+"/comments",
		"--input",
		bodyFile)
	return "Posted protocol minimum version comment.", err
}

func deleteProtocolMinimumVersionComment(
	ctx context.Context,
	config protocolMinimumVersionCommentConfig,
) (bool, error) {
	commentID, err := existingProtocolMinimumVersionComment(ctx, config)
	if err != nil {
		return false, err
	}
	if commentID == "" {
		return false, nil
	}

	_, err = runProtocolMinimumVersionOutput(
		ctx,
		config.repositoryRoot,
		"gh",
		"api",
		"--method",
		"DELETE",
		"repos/"+config.repository+"/issues/comments/"+commentID)
	return err == nil, err
}

func existingProtocolMinimumVersionComment(
	ctx context.Context,
	config protocolMinimumVersionCommentConfig,
) (string, error) {
	output, err := runProtocolMinimumVersionOutput(
		ctx,
		config.repositoryRoot,
		"gh",
		"api",
		"--paginate",
		"repos/"+config.repository+"/issues/"+config.pullRequest+"/comments",
		"--jq",
		".[] | select(.body | contains(\""+protocolMinimumVersionMarker+"\")) | .id")
	if err != nil {
		return "", err
	}

	lines := strings.Split(strings.TrimSpace(output), "\n")
	for index := len(lines) - 1; index >= 0; index-- {
		line := strings.TrimSpace(lines[index])
		if line != "" {
			return line, nil
		}
	}
	return "", nil
}

func writeProtocolMinimumVersionBodyFile(body string) (string, func(), error) {
	bodyFile, err := os.CreateTemp("", "uloop-protocol-minimum-version-warning-*.json")
	if err != nil {
		return "", func() {}, fmt.Errorf("failed to create comment body file: %w", err)
	}
	cleanup := func() { _ = os.Remove(bodyFile.Name()) }

	err = json.NewEncoder(bodyFile).Encode(struct {
		Body string `json:"body"`
	}{Body: body})
	closeErr := bodyFile.Close()
	if err != nil || closeErr != nil {
		cleanup()
		if err != nil {
			return "", func() {}, fmt.Errorf("failed to encode comment body: %w", err)
		}
		return "", func() {}, fmt.Errorf("failed to close comment body file: %w", closeErr)
	}
	return bodyFile.Name(), cleanup, nil
}
