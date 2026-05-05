# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 0.50.x  | :white_check_mark: |
| < 0.50  | :x:                |

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue, please follow these steps:

1. **DO NOT** create a public GitHub issue for security vulnerabilities
2. Use GitHub's [Private Vulnerability Reporting](https://github.com/hatayama/unity-cli-loop/security/advisories/new) feature
3. Or contact the maintainer directly

### What to Include

When reporting a vulnerability, please provide:

- A description of the vulnerability
- Steps to reproduce the issue
- Potential impact of the vulnerability
- Any suggested fixes (if available)

### Response Timeline

- We will acknowledge receipt of your report within **3 business days**
- We will provide an initial assessment within **7 business days**
- We aim to release a fix within **30 days** for critical vulnerabilities

### Security Measures

This project implements several security measures:

- **Dynamic Code Execution Security Levels**: The `execute-dynamic-code` tool supports 3-tier security control (Disabled, Restricted, FullAccess)
- **Security Settings UI**: Tools like `run-tests` and third-party tools are disabled by default
- **Automated Security Scanning**: We use GitHub's security scanning features and custom security analysis tools

### Scope

The following are considered in scope for security reports:

- Code injection vulnerabilities
- Authentication/Authorization bypasses
- Information disclosure
- Denial of service vulnerabilities
- Dependency vulnerabilities

### Out of Scope

- Vulnerabilities that require physical access to the user's machine
- Social engineering attacks
- Issues in third-party dependencies (please report these to the respective maintainers)

## Security Best Practices for Users

When using Unity CLI Loop, we recommend:

1. **Use Restricted Mode**: Set Dynamic Code Security Level to "Restricted" (Level 1) unless you specifically need full access
2. **Review Third-Party Tools**: Only enable "Allow Third-Party Tools" for trusted extensions
3. **Sandbox Environment**: For AI-driven development, consider running in sandbox environments or containers
4. **Keep Updated**: Always use the latest version to benefit from security patches
