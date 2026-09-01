package automation

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"net/url"
	"strings"
)

type wingetContentResponse struct {
	SHA string `json:"sha"`
}

type wingetPullRequestResponse struct {
	HTMLURL string `json:"html_url"`
}

func wingetUpstreamPathExists(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	path string,
) (bool, error) {
	_, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"repos/"+wingetUpstreamRepo+"/contents/"+path+"?ref="+wingetUpstreamBranch,
	)
	if err != nil {
		if strings.Contains(err.Error(), "HTTP 404") {
			return false, nil
		}
		return false, err
	}
	return true, nil
}

func syncWingetFork(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkRepo string,
) error {
	_, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"-X",
		"POST",
		"repos/"+forkRepo+"/merge-upstream",
		"-f",
		"branch="+wingetUpstreamBranch,
	)
	return err
}

func wingetUpstreamMasterSHA(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
) (string, error) {
	output, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"repos/"+wingetUpstreamRepo+"/git/ref/heads/"+wingetUpstreamBranch,
		"--jq",
		".object.sha",
	)
	if err != nil {
		return "", err
	}
	sha := strings.TrimSpace(output)
	if sha == "" {
		return "", fmt.Errorf("winget upstream %s SHA is empty", wingetUpstreamBranch)
	}
	return sha, nil
}

func ensureWingetBranch(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkRepo string,
	branch string,
	sha string,
) error {
	_, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"-X",
		"POST",
		"repos/"+forkRepo+"/git/refs",
		"-f",
		"ref=refs/heads/"+branch,
		"-f",
		"sha="+sha,
	)
	if err != nil && strings.Contains(err.Error(), "HTTP 422") && strings.Contains(err.Error(), "Reference already exists") {
		return nil
	}
	return err
}

func putWingetManifestFile(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkRepo string,
	branch string,
	path string,
	version string,
	content string,
) error {
	existingSHA, err := wingetForkContentSHA(ctx, deps, token, forkRepo, branch, path)
	if err != nil {
		return err
	}
	args := []string{
		"api",
		"-X",
		"PUT",
		"repos/" + forkRepo + "/contents/" + path,
		"-f",
		"message=New version: " + wingetPackageIdentifier + " version " + version,
		"-f",
		"content=" + base64.StdEncoding.EncodeToString([]byte(content)),
		"-f",
		"branch=" + branch,
	}
	if existingSHA != "" {
		args = append(args, "-f", "sha="+existingSHA)
	}
	_, err = deps.runOutput(ctx, wingetTokenEnvironment(token), "gh", args...)
	return err
}

func wingetForkContentSHA(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkRepo string,
	branch string,
	path string,
) (string, error) {
	output, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"repos/"+forkRepo+"/contents/"+path+"?ref="+url.QueryEscape(branch),
	)
	if err != nil {
		if strings.Contains(err.Error(), "HTTP 404") {
			return "", nil
		}
		return "", err
	}
	response := wingetContentResponse{}
	if err = json.Unmarshal([]byte(output), &response); err != nil {
		return "", fmt.Errorf("failed to parse winget fork content response: %w", err)
	}
	return response.SHA, nil
}

func wingetPullRequestOpen(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkOwner string,
	branch string,
) (bool, error) {
	output, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"repos/"+wingetUpstreamRepo+"/pulls?head="+url.QueryEscape(forkOwner+":"+branch)+"&state=open",
	)
	if err != nil {
		return false, err
	}
	pullRequests := []wingetPullRequestResponse{}
	if err = json.Unmarshal([]byte(output), &pullRequests); err != nil {
		return false, fmt.Errorf("failed to parse winget pull request list: %w", err)
	}
	return len(pullRequests) > 0, nil
}

func openWingetPullRequest(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkOwner string,
	branch string,
	title string,
	body string,
) (string, error) {
	output, err := deps.runOutput(
		ctx,
		wingetTokenEnvironment(token),
		"gh",
		"api",
		"-X",
		"POST",
		"repos/"+wingetUpstreamRepo+"/pulls",
		"-f",
		"title="+title,
		"-f",
		"head="+forkOwner+":"+branch,
		"-f",
		"base="+wingetUpstreamBranch,
		"-f",
		"body="+body,
	)
	if err != nil {
		return "", err
	}
	response := wingetPullRequestResponse{}
	if err = json.Unmarshal([]byte(output), &response); err != nil {
		return "", fmt.Errorf("failed to parse created winget pull request: %w", err)
	}
	if response.HTMLURL == "" {
		return "", fmt.Errorf("created winget pull request URL is empty")
	}
	return response.HTMLURL, nil
}

func wingetTokenEnvironment(token string) []string {
	return []string{"GH_TOKEN=" + token}
}
