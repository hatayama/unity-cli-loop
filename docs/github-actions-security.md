# GitHub Actions security policy

Remote GitHub Actions must be pinned to full commit SHAs. Version tags such as
`actions/checkout@v6` are not allowed in workflow files because tag movement can
change CI behavior without a repository diff.

When updating a pinned action:

1. Resolve the intended upstream tag to a commit SHA.
2. Replace the workflow `uses:` ref with the full 40-character SHA.
3. Run `go test ./internal/architecture -run 'TestWorkflowActions|TestPullRequestWorkflow' -count=1` from `cli`.

For nested action paths such as `github/codeql-action/upload-sarif`, resolve the
tag against the action repository, not the nested action path.

Pull request workflows must not restore Go module caches through `setup-go`.
Use `cache: false` for `actions/setup-go` in workflows triggered by
`pull_request` or `pull_request_target`.

Unity `actions/cache` steps in pull request workflows must stay behind the Unity
license secret guard so forked pull requests cannot use those cache entries.
