# run-tests XML result file

When a test or suite fails or a test ends inconclusive, run-tests saves NUnit XML results to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml` and returns the path in `XmlPath`. A run in which every test passed or was skipped saves no XML, and `XmlPath` is `null`.

The XML contains per-test-case results including:

- Test name and full name
- Pass/fail/skip status and duration
- For failed tests: `<message>` (assertion error) and `<stack-trace>`
- For inconclusive tests: `<reason><message>` (the assumption that was not met)
- For failed suites: `<failure>` with `<message>` and `<stack-trace>` on the `<test-suite>`. A fixture whose `OneTimeTearDown` threw keeps its error only here and in `FailedSuites`, because its test cases stay passed.

The response lists at most 10 entries in each of `FailedTests`, `InconclusiveTests`, and `FailedSuites`; the XML keeps every one.
