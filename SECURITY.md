# Security policy

## Supported versions

Security fixes are provided for the latest published DragonDeskPet release and the current `main` branch. Older releases may not receive fixes.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Current `main` | Yes |
| Older releases | No guaranteed support |

## Reporting a vulnerability

Please do not disclose a suspected vulnerability in a public issue, discussion, pull request, or crash log attachment.

Use the repository's **Security** tab and **Report a vulnerability** option when private vulnerability reporting is available. If that option is unavailable, contact the repository owner through GitHub without posting exploit details publicly and request a private reporting channel.

Include only the information needed to reproduce and assess the issue:

- affected DragonDeskPet version or commit;
- Windows version and architecture;
- concise reproduction steps;
- expected and observed behavior;
- potential security or privacy impact;
- sanitized logs or screenshots, if useful.

Never include a real API key, authorization header, chat content, personal file, or other private data. Revoke and rotate any credential that may already have been exposed.

Reports are reviewed on a best-effort basis. Please allow time to confirm the issue and prepare a fix before public disclosure.

## Security-sensitive areas

Reports involving these areas are especially useful:

- Windows user-scoped API-key protection;
- unintended network transmission or logging of private content;
- provider URL and authorization handling;
- startup registration or single-instance IPC abuse;
- unsafe file paths, overwrite behavior, or privilege escalation;
- crash-log secret redaction.

General bugs and feature requests that do not expose private information may be reported through GitHub Issues.
